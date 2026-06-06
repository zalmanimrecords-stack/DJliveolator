using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace Liveolator.App.Features.Shared;

/// <summary>
/// The right-click menu for one track row, bound entirely from the row's own DataContext (so the
/// context flyout needs no fragile ancestor bindings). Delegates to the shared
/// <see cref="TrackContextActions"/>, closing over this row's track path.
/// </summary>
public sealed class TrackMenuViewModel
{
    private readonly string _trackPath;
    private readonly double _bpm;
    private readonly TrackContextActions _actions;

    /// <param name="bpm">The track's analyzed tempo (0 = unknown), fed to the deck as its Sync reference (doc 11).</param>
    public TrackMenuViewModel(string trackPath, TrackContextActions actions, double bpm = 0)
    {
        _trackPath = trackPath ?? throw new ArgumentNullException(nameof(trackPath));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _bpm = bpm;

        LoadToDeckACommand = ReactiveCommand.Create(
            () => _actions.LoadToDeck(0, _trackPath, _bpm), Observable.Return(_actions.CanLoadToDeckA));
        LoadToDeckBCommand = ReactiveCommand.Create(
            () => _actions.LoadToDeck(1, _trackPath, _bpm), Observable.Return(_actions.CanLoadToDeckB));
    }

    public ReactiveCommand<Unit, Unit> LoadToDeckACommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckBCommand { get; }

    /// <summary>Drives the "Add to Deck B" item's visibility (hidden until a second deck is backed).</summary>
    public bool CanLoadToDeckB => _actions.CanLoadToDeckB;

    /// <summary>
    /// The "Add to playlist" submenu, built on demand (when the flyout opens): one entry per saved set
    /// (append this track) plus a "New set with this track" entry.
    /// </summary>
    public IReadOnlyList<MenuActionViewModel> AddToPlaylistItems
    {
        get
        {
            var items = new List<MenuActionViewModel>();
            foreach (string name in _actions.Playlists)
            {
                string target = name; // capture per iteration
                items.Add(new MenuActionViewModel(
                    target,
                    ReactiveCommand.CreateFromTask(() => _actions.AddToPlaylistAsync(_trackPath, target))));
            }
            items.Add(new MenuActionViewModel(
                "New set with this track…",
                ReactiveCommand.CreateFromTask(() => _actions.AddToNewPlaylistAsync(_trackPath))));
            return items;
        }
    }
}
