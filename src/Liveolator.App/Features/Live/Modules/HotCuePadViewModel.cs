using System;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// One hot-cue pad on a deck (the mock's 1·2·3·4 row). Pressing it emits a
/// <see cref="Liveolator.Core.Actions.PerformanceActionKind.DeckHotCue"/> for its index via the supplied
/// callback (the doc 04 seam); <see cref="IsSet"/> follows the deck's hot-cue feedback (the LED model) so a
/// pad lights once its cue is set. A null callback means "no engine backs the deck": the pad is disabled.
/// </summary>
public sealed class HotCuePadViewModel : ViewModelBase
{
    private bool _isSet;

    /// <param name="index">Zero-based hot-cue index (rides in the action <c>Argument</c>).</param>
    /// <param name="onPressed">Invoked on press; null disables the pad.</param>
    public HotCuePadViewModel(int index, Action? onPressed)
    {
        Index = index;
        Label = (index + 1).ToString(); // pads read 1..4 in the UI
        IsEnabled = onPressed is not null;
        TriggerCommand = ReactiveCommand.Create(
            () => onPressed?.Invoke(), Observable.Return(IsEnabled));
    }

    /// <summary>Zero-based hot-cue index this pad addresses.</summary>
    public int Index { get; }

    /// <summary>Pad label shown in the UI (1-based).</summary>
    public string Label { get; }

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
}
