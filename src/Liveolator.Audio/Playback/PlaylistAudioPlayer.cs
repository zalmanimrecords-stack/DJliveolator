using Liveolator.Core.Audio;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Binds the pure <see cref="ILivePlaylist"/> queue to the realtime engine: on every
/// <see cref="ILivePlaylist.NowChanged"/> it drives the underlying player to the new <c>Now</c> track
/// (load + play on the bound deck slot), or stops the slot when the queue runs dry (doc 09). The
/// queue stays the single source of truth for sequencing; this is the audio side of the seam.
/// </summary>
/// <remarks>
/// All track handoff is wrapped: a failed load is logged with the track path and dropped, never
/// thrown — a bad track must not crash the show or stall the queue (global standards #16/#26).
/// </remarks>
public sealed class PlaylistAudioPlayer : IDisposable
{
    private readonly ILivePlaylist _playlist;
    private readonly IMultiDeckPlaybackEngine _engine;
    private readonly int _slot;
    private readonly bool _autoPlay;
    private readonly ILogger<PlaylistAudioPlayer> _logger;
    private bool _disposed;

    /// <param name="playlist">The queue whose <c>Now</c> drives playback.</param>
    /// <param name="engine">The deck engine driven on each <c>NowChanged</c>.</param>
    /// <param name="slot">The deck slot the queue plays on (defaults to A = 0).</param>
    /// <param name="autoPlay">When true the loaded track starts immediately; otherwise it is loaded paused.</param>
    public PlaylistAudioPlayer(
        ILivePlaylist playlist,
        IMultiDeckPlaybackEngine engine,
        int slot = 0,
        bool autoPlay = true,
        ILogger<PlaylistAudioPlayer>? logger = null)
    {
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (slot < 0 || slot >= engine.DeckCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range for the engine.");

        _slot = slot;
        _autoPlay = autoPlay;
        _logger = logger ?? NullLogger<PlaylistAudioPlayer>.Instance;

        _playlist.NowChanged += OnNowChanged;

        // Pick up a track that is already Now (the queue may have been loaded before binding).
        if (_playlist.Now is { } current)
            GoToTrack(current);
    }

    private void OnNowChanged(object? sender, QueueEntry? now) => GoToTrack(now);

    // Drives the engine to the given Now track. Tolerant: a failed load/play is logged and dropped so
    // the queue keeps advancing. A null Now (queue exhausted) stops the deck without an error.
    private void GoToTrack(QueueEntry? now)
    {
        if (now is null)
        {
            try
            {
                _engine.Stop(_slot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop deck {Slot} after the live queue ran dry.", _slot);
            }
            return;
        }

        try
        {
            _engine.Load(_slot, now.TrackPath);
            if (_autoPlay && !_engine.IsPlaying(_slot))
                _engine.PlayPause(_slot);
        }
        catch (Exception ex)
        {
            // Degrade: log and continue. The queue has already advanced past this entry, so the next
            // NowChanged (e.g. an auto-advance) can still play — a bad track does not kill the show.
            _logger.LogError(ex, "Failed to load live-queue track '{TrackPath}' onto deck {Slot}.", now.TrackPath, _slot);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _playlist.NowChanged -= OnNowChanged;
    }
}
