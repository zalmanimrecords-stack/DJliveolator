using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Playlist;

/// <summary>
/// The dispatcher handler owning the playlist actions, translating them into
/// <see cref="ILivePlaylist"/> edits (doc 04/09). Edits carrying an id/path use the action's
/// <c>Argument</c>/<c>Slot</c>; malformed arguments from a laggy source are logged at debug and
/// ignored rather than thrown (the dispatcher would otherwise log them as errors).
/// </summary>
public sealed class PlaylistActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.PlaylistInsertTrackNext,
        PerformanceActionKind.PlaylistMoveTrack,
        PerformanceActionKind.PlaylistRemoveFutureTrack,
        PerformanceActionKind.PlaylistSkipOnNextBar,
    };

    private readonly ILivePlaylist _playlist;
    private readonly ILogger<PlaylistActionHandler> _logger;

    public PlaylistActionHandler(ILivePlaylist playlist, ILogger<PlaylistActionHandler> logger)
    {
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
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
                _playlist.SkipOn(Quantize.NextBar);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    private void InsertNext(PerformanceAction action)
    {
        if (string.IsNullOrEmpty(action.Argument))
        {
            _logger.LogDebug("PlaylistInsertTrackNext ignored: no track path in Argument.");
            return;
        }

        _playlist.InsertNext(action.Argument);
    }

    private void Move(PerformanceAction action)
    {
        if (!TryGetId(action, out Guid id))
            return;
        _playlist.Move(id, action.Slot);
    }

    private void RemoveFuture(PerformanceAction action)
    {
        if (!TryGetId(action, out Guid id))
            return;
        _playlist.RemoveFuture(id);
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
