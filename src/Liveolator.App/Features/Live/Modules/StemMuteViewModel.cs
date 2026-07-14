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
        Icon = IconFor(kind);
        ToggleCommand = ReactiveCommand.Create(
            () => onToggle?.Invoke(), Observable.Return(onToggle is not null));
    }

    /// <summary>The stem this button addresses.</summary>
    public StemKind Kind { get; }

    /// <summary>The stem name, upper-cased — used as the button tooltip now the face shows an icon.</summary>
    public string Name { get; }

    /// <summary>Vector-path glyph (a 24×24 SVG mini-language string) drawn on the button face instead of
    /// text, so the tiny 2×2 stem cluster stays legible. Fill follows the button foreground (lit/unlit).</summary>
    public string Icon { get; }

    // Simple monochrome silhouettes, one per stem: drum ring, speaker (bass), microphone (vocals),
    // eighth-note (other). Drawn in a 0..24 box; the view scales them uniformly to the button.
    private static string IconFor(StemKind kind) => kind switch
    {
        StemKind.Drums => "F0 M2.5,12 A9.5,9.5 0 1 1 21.5,12 A9.5,9.5 0 1 1 2.5,12 Z "
            + "M7.5,12 A4.5,4.5 0 1 1 16.5,12 A4.5,4.5 0 1 1 7.5,12 Z",
        StemKind.Bass => "M3,9.5 L7,9.5 L12,5 L12,19 L7,14.5 L3,14.5 Z "
            + "M14,9 C15.6,10.6 15.6,13.4 14,15 L15.1,16.1 C17.3,13.9 17.3,10.1 15.1,7.9 Z "
            + "M16.7,6.3 C19.9,9.5 19.9,14.5 16.7,17.7 L17.8,18.8 C21.6,15 21.6,9 17.8,5.2 Z",
        StemKind.Vocals => "M12,3 C10.6,3 9.5,4.1 9.5,5.5 L9.5,10.5 C9.5,11.9 10.6,13 12,13 "
            + "C13.4,13 14.5,11.9 14.5,10.5 L14.5,5.5 C14.5,4.1 13.4,3 12,3 Z "
            + "M7,10 L8.5,10 C8.5,12.5 10,14 12,14 C14,14 15.5,12.5 15.5,10 L17,10 "
            + "C17,13 14.9,15.3 12,15.3 C9.1,15.3 7,13 7,10 Z "
            + "M11.25,15.3 L12.75,15.3 L12.75,19 L11.25,19 Z M8.5,19 L15.5,19 L15.5,20.5 L8.5,20.5 Z",
        StemKind.Other => "M5,17 A3,2.6 0 1 0 11,17 A3,2.6 0 1 0 5,17 Z "
            + "M10.4,5 L11.9,5 L11.9,17.3 L10.4,17.3 Z "
            + "M11.9,5 C14.5,5.6 16.5,7.4 16.5,10.5 C16.5,8.6 14.5,7.4 11.9,8 Z",
        _ => string.Empty,
    };

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
