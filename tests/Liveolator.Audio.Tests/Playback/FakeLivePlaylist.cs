using System;
using System.Collections.Generic;
using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Hand-driveable <see cref="ILivePlaylist"/> for testing the audio binding against the seam: a test
/// sets <see cref="Now"/>/<see cref="Upcoming"/> and calls <see cref="RaiseNowChanged"/> to simulate
/// the queue advancing, without depending on the real queue's internals.
/// </summary>
internal sealed class FakeLivePlaylist : ILivePlaylist
{
    public QueueEntry? Now { get; set; }

    public IReadOnlyList<QueueEntry> Upcoming { get; set; } = Array.Empty<QueueEntry>();

    public bool AutoAdvance { get; private set; } = true;

    public event EventHandler<QueueEntry?>? NowChanged;

    /// <summary>Simulates the queue moving to <paramref name="now"/> and notifying subscribers.</summary>
    public void RaiseNowChanged(QueueEntry? now)
    {
        Now = now;
        NowChanged?.Invoke(this, now);
    }

    public void Load(IEnumerable<string> trackPaths) { }
    public void Append(string trackPath) { }
    public void InsertNext(string trackPath) { }
    public void Move(Guid id, int toIndex) { }
    public void RemoveFuture(Guid id) { }
    public void SetAutoAdvance(bool on) => AutoAdvance = on;
    public void SkipNow() { }
    public void SkipOn(Quantize when, int everyN = 1) { }

    /// <summary>How many times the binding signalled end-of-track (A4 auto-advance assertions).</summary>
    public int NotifyTrackEndedCount { get; private set; }

    public void NotifyTrackEnded() => NotifyTrackEndedCount++;
}
