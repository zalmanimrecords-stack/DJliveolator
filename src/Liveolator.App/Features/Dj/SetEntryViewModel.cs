using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// One row of the DJ "set" — a track in the live Now/Next/Later queue (doc 09). Display-only state
/// (title + position) plus a Remove command. Removing is driven through the dispatcher by the parent
/// (a <see cref="PerformanceActionKind.PlaylistRemoveFutureTrack"/> action); the playing "Now" entry is
/// protected, so its remove callback is null and the button is disabled.
/// </summary>
public sealed class SetEntryViewModel : ViewModelBase
{
    public SetEntryViewModel(QueueEntry entry, string title, Action? onRemove)
    {
        Id = entry.Id;
        Title = title;
        State = entry.State;
        IsNow = entry.State == TrackState.Now;
        RemoveCommand = ReactiveCommand.Create(
            () => onRemove?.Invoke(), Observable.Return(onRemove is not null));
    }

    /// <summary>Stable queue-slot id, used to reorder/remove without depending on position.</summary>
    public Guid Id { get; }

    /// <summary>Track title (from the catalog), or the file name when unknown.</summary>
    public string Title { get; }

    /// <summary>Queue position of this entry.</summary>
    public TrackState State { get; }

    /// <summary>True for the currently-playing entry (drives the accent + protects it from removal).</summary>
    public bool IsNow { get; }

    /// <summary>Uppercase position label for the row badge (NOW / NEXT / LATER).</summary>
    public string StateLabel => State.ToString().ToUpperInvariant();

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
}
