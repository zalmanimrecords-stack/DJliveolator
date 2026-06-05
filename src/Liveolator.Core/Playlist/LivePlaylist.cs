using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Playlist;

/// <summary>
/// In-memory Now/Next/Later queue. Pure live-editing logic with no audio: it owns the ordered
/// future and raises <see cref="NowChanged"/> so the audio binding advances the underlying player.
/// Edits to the future never disturb Now, and stale ids from a laggy UI are ignored rather than
/// thrown (doc 09, global standards #7/#26).
/// </summary>
public sealed class LivePlaylist : ILivePlaylist
{
    private readonly IBeatScheduler _scheduler;
    private readonly ILogger<LivePlaylist> _logger;
    private readonly List<Item> _upcoming = new();
    private Item? _now;
    private bool _autoAdvance = true;

    public LivePlaylist(IBeatScheduler scheduler, ILogger<LivePlaylist> logger)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler<QueueEntry?>? NowChanged;

    /// <inheritdoc />
    public QueueEntry? Now => _now is { } item ? new QueueEntry(item.Path, item.Id, TrackState.Now) : null;

    /// <inheritdoc />
    public IReadOnlyList<QueueEntry> Upcoming
        => _upcoming
            .Select((item, index) => new QueueEntry(item.Path, item.Id, index == 0 ? TrackState.Next : TrackState.Later))
            .ToList();

    /// <inheritdoc />
    public bool AutoAdvance => _autoAdvance;

    /// <inheritdoc />
    public void Load(IEnumerable<string> trackPaths)
    {
        ArgumentNullException.ThrowIfNull(trackPaths);

        _upcoming.Clear();
        _now = null;
        foreach (string path in trackPaths)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            _upcoming.Add(new Item(Guid.NewGuid(), path));
        }

        PullNextIntoNow();
        RaiseNowChanged();
    }

    /// <inheritdoc />
    public void Append(string trackPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackPath);
        _upcoming.Add(new Item(Guid.NewGuid(), trackPath));
    }

    /// <inheritdoc />
    public void InsertNext(string trackPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackPath);
        _upcoming.Insert(0, new Item(Guid.NewGuid(), trackPath));
    }

    /// <inheritdoc />
    public void Move(Guid id, int toIndex)
    {
        int from = _upcoming.FindIndex(item => item.Id == id);
        if (from < 0)
        {
            _logger.LogDebug("Move ignored: no upcoming entry with id {Id}.", id);
            return;
        }

        Item item = _upcoming[from];
        _upcoming.RemoveAt(from);
        int target = Math.Clamp(toIndex, 0, _upcoming.Count);
        _upcoming.Insert(target, item);
    }

    /// <inheritdoc />
    public void RemoveFuture(Guid id)
    {
        if (_now is { } now && now.Id == id)
        {
            _logger.LogDebug("RemoveFuture ignored: id {Id} is the playing track (protected).", id);
            return;
        }

        int removed = _upcoming.RemoveAll(item => item.Id == id);
        if (removed == 0)
            _logger.LogDebug("RemoveFuture ignored: no upcoming entry with id {Id}.", id);
    }

    /// <inheritdoc />
    public void SetAutoAdvance(bool on) => _autoAdvance = on;

    /// <inheritdoc />
    public void SkipNow() => Advance();

    /// <inheritdoc />
    public void SkipOn(Quantize when, int everyN = 1) => _scheduler.Schedule(when, everyN, SkipNow);

    /// <inheritdoc />
    public void NotifyTrackEnded()
    {
        if (_autoAdvance)
            Advance();
    }

    private void Advance()
    {
        PullNextIntoNow();
        RaiseNowChanged();
    }

    private void PullNextIntoNow()
    {
        if (_upcoming.Count > 0)
        {
            _now = _upcoming[0];
            _upcoming.RemoveAt(0);
        }
        else
        {
            _now = null;
        }
    }

    private void RaiseNowChanged() => NowChanged?.Invoke(this, Now);

    private readonly record struct Item(Guid Id, string Path);
}
