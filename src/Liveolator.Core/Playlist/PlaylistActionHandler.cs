using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Playlist;

/// <summary>
/// The dispatcher handler owning the playlist actions, translating them into
/// <see cref="ILivePlaylist"/> edits (doc 04/09). Each deck slot can have its own live queue
/// (A = 0, B = 1): append/insert/remove/skip address a queue via the action's <c>Slot</c>.
/// <see cref="PerformanceActionKind.PlaylistMoveTrack"/> is the exception — its <c>Slot</c> is the
/// target index (the pre-existing contract), so it always edits deck A's queue. Malformed
/// arguments or an unbacked slot from a laggy source are logged at debug and ignored rather than
/// thrown (the dispatcher would otherwise log them as errors).
/// </summary>
public sealed class PlaylistActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.PlaylistAppendTrack,
        PerformanceActionKind.PlaylistInsertTrackNext,
        PerformanceActionKind.PlaylistMoveTrack,
        PerformanceActionKind.PlaylistRemoveFutureTrack,
        PerformanceActionKind.PlaylistSkipOnNextBar,
    };

    private readonly IReadOnlyList<ILivePlaylist> _playlists;
    private readonly ILogger<PlaylistActionHandler> _logger;

    /// <summary>Single-queue composition: every playlist action edits the one queue (deck A).</summary>
    public PlaylistActionHandler(ILivePlaylist playlist, ILogger<PlaylistActionHandler> logger)
        : this(new[] { playlist ?? throw new ArgumentNullException(nameof(playlist)) }, logger)
    {
    }

    /// <summary>Per-deck queues, indexed by deck slot (A = 0, B = 1).</summary>
    public PlaylistActionHandler(IReadOnlyList<ILivePlaylist> playlists, ILogger<PlaylistActionHandler> logger)
    {
        _playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        if (_playlists.Count == 0 || _playlists.Any(p => p is null))
            throw new ArgumentException("At least one non-null live playlist is required.", nameof(playlists));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case PerformanceActionKind.PlaylistAppendTrack:
                AppendForSlot(action);
                break;
            case PerformanceActionKind.PlaylistInsertTrackNext:
                InsertNext(action);
                break;
            case PerformanceActionKind.PlaylistMoveTrack:
                Move(action);
                break;
            case PerformanceActionKind.PlaylistRemoveFutureTrack:
                RemoveFuture(action);
                break;
            case PerformanceActionKind.PlaylistSkipOnNextBar:
                PlaylistFor(action)?.SkipOn(Quantize.NextBar);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    private void AppendForSlot(PerformanceAction action)
    {
        if (string.IsNullOrEmpty(action.Argument))
        {
            _logger.LogDebug("PlaylistAppendTrack ignored: no track path in Argument.");
            return;
        }

        PlaylistFor(action)?.Append(action.Argument);
    }

    private void InsertNext(PerformanceAction action)
    {
        if (string.IsNullOrEmpty(action.Argument))
        {
            _logger.LogDebug("PlaylistInsertTrackNext ignored: no track path in Argument.");
            return;
        }

        PlaylistFor(action)?.InsertNext(action.Argument);
    }

    private void Move(PerformanceAction action)
    {
        if (!TryGetId(action, out Guid id))
            return;
        // Move's Slot carries the target index (pre-existing contract) — it edits deck A's queue.
        _playlists[0].Move(id, action.Slot);
    }

    private void RemoveFuture(PerformanceAction action)
    {
        if (!TryGetId(action, out Guid id))
            return;
        PlaylistFor(action)?.RemoveFuture(id);
    }

    // Resolves the queue the action's Slot addresses; an unbacked slot (e.g. deck B before a second
    // queue is composed) is logged at debug and dropped — never thrown back at the dispatcher.
    private ILivePlaylist? PlaylistFor(PerformanceAction action)
    {
        if (action.Slot >= 0 && action.Slot < _playlists.Count)
            return _playlists[action.Slot];

        _logger.LogDebug(
            "{Kind} ignored: no live queue backs deck slot {Slot}.", action.Kind, action.Slot);
        return null;
    }

    private bool TryGetId(PerformanceAction action, out Guid id)
    {
        if (Guid.TryParse(action.Argument, out id))
            return true;

        _logger.LogDebug("{Kind} ignored: Argument '{Argument}' is not a valid entry id.",
            action.Kind, action.Argument);
        return false;
    }
}
