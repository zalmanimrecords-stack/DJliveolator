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
    private readonly double _firstBeatSeconds;
    private readonly TrackContextActions _actions;

    /// <param name="bpm">The track's analyzed tempo (0 = unknown), fed to the deck as its Sync reference (doc 11).</param>
    /// <param name="firstBeatSeconds">The analyzed downbeat anchor (0 = unknown), fed to phase-match (doc 22 A1).</param>
    public TrackMenuViewModel(string trackPath, TrackContextActions actions, double bpm = 0, double firstBeatSeconds = 0)
    {
        _trackPath = trackPath ?? throw new ArgumentNullException(nameof(trackPath));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _bpm = bpm;
        _firstBeatSeconds = firstBeatSeconds;

        LoadToDeckACommand = ReactiveCommand.Create(
            () => _actions.LoadToDeck(0, _trackPath, _bpm, _firstBeatSeconds), Observable.Return(_actions.CanLoadToDeckA));
        LoadToDeckBCommand = ReactiveCommand.Create(
            () => _actions.LoadToDeck(1, _trackPath, _bpm, _firstBeatSeconds), Observable.Return(_actions.CanLoadToDeckB));
        AnalyzeAgainCommand = ReactiveCommand.CreateFromTask(
            () => _actions.AnalyzeAgainAsync(_trackPath), Observable.Return(_actions.CanAnalyze));
        EditMetadataCommand = ReactiveCommand.CreateFromTask(
            () => _actions.EditAsync(_trackPath), Observable.Return(_actions.CanEdit));
        AutoCueCommand = ReactiveCommand.CreateFromTask(
            () => _actions.AutoCueAsync(_trackPath), Observable.Return(_actions.CanAutoCue));
    }

    public ReactiveCommand<Unit, Unit> LoadToDeckACommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckBCommand { get; }
    public ReactiveCommand<Unit, Unit> AnalyzeAgainCommand { get; }
    public ReactiveCommand<Unit, Unit> EditMetadataCommand { get; }

    /// <summary>Places automatic hot cues for this track (persisted; they appear on the next deck load).</summary>
    public ReactiveCommand<Unit, Unit> AutoCueCommand { get; }

    /// <summary>Drives the "Auto-cue track" menu item's visibility (hidden when no decoder/cue store).</summary>
    public bool CanAutoCue => _actions.CanAutoCue;

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
