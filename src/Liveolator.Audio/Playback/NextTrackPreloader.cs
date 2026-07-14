using Liveolator.Core.Audio;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Keeps the upcoming track warm: on every <see cref="ILivePlaylist.NowChanged"/> it asks the
/// <see cref="IDeckPreloader"/> to preload <c>Upcoming[0]</c> (the next track), so advancing is
/// near-instant (doc 09 "Preload"). Editing the future via a live reorder also changes
/// <c>Upcoming[0]</c>; the preloader supersedes the in-flight preload through the seam.
/// </summary>
/// <remarks>
/// Tolerant: a failure to schedule a preload is logged and dropped, never thrown — preloading is a
/// best-effort latency optimization and must not crash the show or stall the queue
/// (global standards #16/#26).
/// </remarks>
public sealed class NextTrackPreloader : IDisposable
{
    private readonly ILivePlaylist _playlist;
    private readonly IDeckPreloader _preloader;
    private readonly ILogger<NextTrackPreloader> _logger;
    private bool _disposed;

    public NextTrackPreloader(
        ILivePlaylist playlist,
        IDeckPreloader preloader,
        ILogger<NextTrackPreloader>? logger = null)
    {
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _preloader = preloader ?? throw new ArgumentNullException(nameof(preloader));
        _logger = logger ?? NullLogger<NextTrackPreloader>.Instance;

        _playlist.NowChanged += OnNowChanged;

        // Warm the next track for an already-loaded queue.
        PreloadNext();
    }

    private void OnNowChanged(object? sender, QueueEntry? now) => PreloadNext();

    // Hands the next upcoming track (or null when the future is empty) to the preload seam.
    private void PreloadNext()
    {
        try
        {
            IReadOnlyList<QueueEntry> upcoming = _playlist.Upcoming;
            string? next = upcoming.Count > 0 ? upcoming[0].TrackPath : null;
            _preloader.Preload(next);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preload the next live-queue track.");
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
