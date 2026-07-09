using System;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Analysis.Stems;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// One per-stem mute button on a deck's channel strip (doc 32 §Phase 2b). Pressing it emits a
/// <see cref="Liveolator.Core.Actions.PerformanceActionKind.DeckStemMute"/> for its stem via the supplied
/// callback (the doc 04 seam); the engine toggles the mute. The button follows the deck's stem feedback:
/// <see cref="IsAudible"/> lights it while the stem is playing (lit = audible, the design line), and
/// <see cref="IsAvailable"/> enables it only while the deck is actually a 4-stem deck. A null callback
/// (no engine backing the deck) leaves it permanently disabled.
/// </summary>
public sealed class StemMuteViewModel : ViewModelBase
{
    private bool _isAudible = true;
    private bool _isAvailable;

    /// <param name="kind">The stem this button mutes (rides in the action <c>Argument</c>).</param>
    /// <param name="onToggle">Invoked on press; null disables the button.</param>
    public StemMuteViewModel(StemKind kind, Action? onToggle)
    {
        Kind = kind;
        Name = kind.ToString().ToUpperInvariant(); // DRUMS / BASS / VOCALS / OTHER
        ToggleCommand = ReactiveCommand.Create(
            () => onToggle?.Invoke(), Observable.Return(onToggle is not null));
    }

    /// <summary>The stem this button addresses.</summary>
    public StemKind Kind { get; }

    /// <summary>The button label (the stem name, upper-cased).</summary>
    public string Name { get; }

    /// <summary>Emits the stem-mute toggle for <see cref="Kind"/>.</summary>
    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    /// <summary>True while the stem is playing (lights the button); false = muted. From deck feedback.</summary>
    public bool IsAudible
    {
        get => _isAudible;
        private set => this.RaiseAndSetIfChanged(ref _isAudible, value);
    }

    /// <summary>True only when the deck is a 4-stem deck; the UI disables the button otherwise.</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isAvailable, value);
    }

    /// <summary>Apply the button's state from a deck stem-mute feedback echo (presentation only).</summary>
    public void SetState(bool audible, bool available)
    {
        IsAudible = audible;
        IsAvailable = available;
    }
}
