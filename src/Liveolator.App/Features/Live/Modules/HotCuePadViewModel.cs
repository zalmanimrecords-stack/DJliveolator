using System;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// One hot-cue pad on a deck (the mock's 1·2·3·4 row). Pressing it emits a
/// <see cref="Liveolator.Core.Actions.PerformanceActionKind.DeckHotCue"/> for its index via the supplied
/// callback (the doc 04 seam). The pad follows the deck's hot-cue feedback: <see cref="IsSet"/> lights it
/// once its cue is set, and <see cref="CueLabel"/>/<see cref="Color"/>/<see cref="IsAuto"/> let it show the
/// cue's name and color (e.g. a red "Drop") and mark an unconfirmed suggestion — not just a lit number.
/// A null callback means "no engine backs the deck": the pad is disabled.
/// </summary>
public sealed class HotCuePadViewModel : ViewModelBase
{
    private bool _isSet;
    private string? _cueLabel;
    private int? _color;
    private bool _isAuto;

    /// <param name="index">Zero-based hot-cue index (rides in the action <c>Argument</c>).</param>
    /// <param name="onPressed">Invoked on press; null disables the pad.</param>
    public HotCuePadViewModel(int index, Action? onPressed)
    {
        Index = index;
        Number = (index + 1).ToString(); // pads read 1..N in the UI
        IsEnabled = onPressed is not null;
        TriggerCommand = ReactiveCommand.Create(
            () => onPressed?.Invoke(), Observable.Return(IsEnabled));
    }

    /// <summary>Zero-based hot-cue index this pad addresses.</summary>
    public int Index { get; }

    /// <summary>The 1-based pad number, shown when the cue has no performer label.</summary>
    public string Number { get; }

    /// <summary>True when the pad can emit; the UI disables it otherwise.</summary>
    public bool IsEnabled { get; }

    /// <summary>Emits the hot-cue action for <see cref="Index"/>.</summary>
    public ReactiveCommand<Unit, Unit> TriggerCommand { get; }

    /// <summary>True when this cue is set on the loaded track (lights the pad), from deck feedback.</summary>
    public bool IsSet
    {
        get => _isSet;
        set => this.RaiseAndSetIfChanged(ref _isSet, value);
    }

    /// <summary>The cue's performer label (e.g. "Drop"), or null when unset/unlabelled.</summary>
    public string? CueLabel
    {
        get => _cueLabel;
        private set
        {
            if (_cueLabel == value)
                return;
            this.RaiseAndSetIfChanged(ref _cueLabel, value);
            this.RaisePropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>Optional 0xRRGGBB cue color (drives the pad's color accent), or null when unset.</summary>
    public int? Color
    {
        get => _color;
        private set => this.RaiseAndSetIfChanged(ref _color, value);
    }

    /// <summary>True when this cue is an unconfirmed auto-placed suggestion (drives a "suggested" marker).</summary>
    public bool IsAuto
    {
        get => _isAuto;
        private set => this.RaiseAndSetIfChanged(ref _isAuto, value);
    }

    /// <summary>What the pad shows: the cue's label when it has one, otherwise the pad number.</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(CueLabel) ? Number : CueLabel!;

    /// <summary>
    /// Apply the pad's state from a deck hot-cue feedback echo: lit state plus the cue's label/color/auto
    /// metadata. Presentation only — the cue data lives in the engine/store.
    /// </summary>
    public void SetState(bool isSet, string? label, int? color, bool isAuto)
    {
        IsSet = isSet;
        CueLabel = label;
        Color = color;
        IsAuto = isAuto;
    }

    /// <summary>Reset the pad to empty (no cue, no label/color) — used when a new track loads.</summary>
    public void Clear() => SetState(isSet: false, label: null, color: null, isAuto: false);
}
