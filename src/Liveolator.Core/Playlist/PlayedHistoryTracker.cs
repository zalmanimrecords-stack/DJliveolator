namespace Liveolator.Core.Playlist;

/// <summary>
/// Tracks entries that leave Now through a sequential queue advance.
/// A replacement or reload resets the history because it starts a new live set.
/// </summary>
public sealed class PlayedHistoryTracker
{
    private readonly List<QueueEntry> _history = [];
    private QueueEntry? _previousNow;
    private Guid? _expectedNextId;

    public PlayedHistoryTracker(QueueEntry? now, IReadOnlyList<QueueEntry> upcoming)
    {
        CapturePosition(now, upcoming);
    }

    /// <summary>Played entries in most-recent-first order.</summary>
    public IReadOnlyList<QueueEntry> History => _history;

    /// <summary>Observes a changed queue position and reports whether visible history changed.</summary>
    public bool Observe(QueueEntry? now, IReadOnlyList<QueueEntry> upcoming)
    {
        bool changed;
        if (_previousNow is not null && now?.Id == _expectedNextId)
        {
            _history.Insert(0, _previousNow with { State = TrackState.Played });
            changed = true;
        }
        else if (_previousNow is not null || now is not null)
        {
            changed = _history.Count > 0;
            _history.Clear();
        }
        else
        {
            changed = false;
        }

        CapturePosition(now, upcoming);
        return changed;
    }

    private void CapturePosition(QueueEntry? now, IReadOnlyList<QueueEntry> upcoming)
    {
        _previousNow = now;
        _expectedNextId = upcoming.Count > 0 ? upcoming[0].Id : null;
    }
}
