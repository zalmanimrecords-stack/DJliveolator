using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Bpm;
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
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly IMultiDeckPlaybackEngine _engine;
    private readonly Func<string, BpmResult?>? _analysisResolver;
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
        : this(
            playlist,
            new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new DeckActionHandler(engine) },
                NullLogger<PerformanceActionDispatcher>.Instance),
            engine,
            analysisResolver: null,
            slot: slot,
            autoPlay: autoPlay,
            logger: logger)
    {
    }

    public PlaylistAudioPlayer(
        ILivePlaylist playlist,
        IPerformanceActionDispatcher dispatcher,
        IMultiDeckPlaybackEngine engine,
        Func<string, BpmResult?>? analysisResolver = null,
        int slot = 0,
        bool autoPlay = true,
        ILogger<PlaylistAudioPlayer>? logger = null)
    {
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _analysisResolver = analysisResolver;
        if (slot < 0 || slot >= engine.DeckCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range for the engine.");

        _slot = slot;
        _autoPlay = autoPlay;
        _logger = logger ?? NullLogger<PlaylistAudioPlayer>.Instance;

        _playlist.NowChanged += OnNowChanged;
        // End-of-track auto-advance (A4): when the bound deck's track ends, tell the queue so it advances
        // (or stops when dry). The queue then raises NowChanged, which drives the next load via OnNowChanged.
        _engine.DeckEnded += OnDeckEnded;

        // Pick up a track that is already Now (the queue may have been loaded before binding).
        if (_playlist.Now is { } current)
            GoToTrack(current);
    }

    private void OnNowChanged(object? sender, QueueEntry? now) => GoToTrack(now);

    // The deck reached the end of its track. Only the slot this player drives advances the queue; an end
    // on another deck (slot) is ignored. Tolerant: a queue-advance failure is logged, never thrown back
    // onto the engine's end-of-stream thread (global standards #16/#26).
    private void OnDeckEnded(object? sender, int slot)
    {
        if (slot != _slot)
            return;

        try
        {
            _playlist.NotifyTrackEnded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-advance after deck {Slot} ended failed.", _slot);
        }
    }

    // Drives the engine to the given Now track. Tolerant: a failed load/play is logged and dropped so
    // the queue keeps advancing. A null Now (queue exhausted) stops the deck without an error.
    private void GoToTrack(QueueEntry? now)
    {
        if (now is null)
        {
            try
            {
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.TransportStop, Slot: _slot));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop deck {Slot} after the live queue ran dry.", _slot);
            }
            return;
        }

        try
        {
            BpmResult? analysis = ResolveAnalysis(now.TrackPath);
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckLoadTrack,
                ActionInputMode.Absolute,
                Value: analysis?.Bpm ?? 0.0,
                Slot: _slot,
                Argument: now.TrackPath));
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSetFirstBeat,
                ActionInputMode.Absolute,
                Value: analysis?.FirstBeatSeconds ?? 0.0,
                Slot: _slot));
            if (_autoPlay && !_engine.IsPlaying(_slot))
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckPlayPause, Slot: _slot));
        }
        catch (Exception ex)
        {
            // Degrade: log and continue. The queue has already advanced past this entry, so the next
            // NowChanged (e.g. an auto-advance) can still play — a bad track does not kill the show.
            _logger.LogError(ex, "Failed to load live-queue track '{TrackPath}' onto deck {Slot}.", now.TrackPath, _slot);
        }
    }

    private BpmResult? ResolveAnalysis(string trackPath)
    {
        if (_analysisResolver is null)
            return null;

        try
        {
            return _analysisResolver(trackPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Could not resolve beat metadata for live-queue track '{TrackPath}'.", trackPath);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _playlist.NowChanged -= OnNowChanged;
        _engine.DeckEnded -= OnDeckEnded;
    }
}
