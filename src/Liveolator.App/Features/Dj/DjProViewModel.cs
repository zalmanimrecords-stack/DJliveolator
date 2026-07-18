using System;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The DJ PRO tab — a denser, feature-richer DJ performance surface built ALONGSIDE the plain DJ tab
/// (never replacing it). It drives the SAME shared decks + mixer (<see cref="PerformanceDeckSet"/>) and
/// reuses the SAME track browser as the DJ tab (one source of truth, doc 12), so nothing here forks the
/// DJ engine — this view-model only arranges more controls around the shared console: a per-deck external
/// FX rack ("effects outside", via AudioFxSetParameter) and a per-deck STEM level rack ("more knobs for
/// stems", via DeckStemGain). Everything is sized to fit one screen with no scroll at every tier.
/// </summary>
public sealed class DjProViewModel : ViewModelBase, IDisposable
{
    /// <param name="decks">The shared decks + crossfader (doc 11) — the same instance the DJ/LIVE tabs drive.</param>
    /// <param name="dispatcher">The one action layer; drives the per-deck FX racks. Null disables them.</param>
    /// <param name="browser">The shared DJ track browser; null when no catalog is wired (headless/tests).</param>
    public DjProViewModel(
        PerformanceDeckSet decks,
        IPerformanceActionDispatcher? dispatcher = null,
        DjBrowserViewModel? browser = null)
    {
        Decks = decks ?? throw new ArgumentNullException(nameof(decks));
        Browser = browser;
        // The "effects outside" racks — one per live deck (A = slot 0, B = slot 1), driving the same
        // built-in FX chain the DJ tab's FX-mode button drives, but as always-visible knobs.
        FxRackA = new DeckFxRackViewModel(dispatcher, slot: 0);
        FxRackB = new DeckFxRackViewModel(dispatcher, slot: 1);
        // Per-deck stem level knobs (DRUMS/BASS/VOCALS/OTHER) — the "more knobs for stems" request.
        StemRackA = new DeckStemRackViewModel(dispatcher, slot: 0);
        StemRackB = new DeckStemRackViewModel(dispatcher, slot: 1);

        // Deck ◀/▶ browse-and-load: step the shared browser's current list onto that deck (load-or-queue).
        // Disabled when no browser is wired (headless/tests). The decks reach these via DjProDeckView's
        // BrowsePrev/NextCommand — the deck itself never learns about the browser.
        var hasBrowser = Observable.Return(Browser is not null);
        BrowsePrevACommand = ReactiveCommand.Create(() => Browser?.StepAndLoad(0, -1), hasBrowser);
        BrowseNextACommand = ReactiveCommand.Create(() => Browser?.StepAndLoad(0, +1), hasBrowser);
        BrowsePrevBCommand = ReactiveCommand.Create(() => Browser?.StepAndLoad(1, -1), hasBrowser);
        BrowseNextBCommand = ReactiveCommand.Create(() => Browser?.StepAndLoad(1, +1), hasBrowser);
    }

    /// <summary>Deck A: load the previous track from the browser's current list (load-or-queue).</summary>
    public ReactiveCommand<Unit, Unit> BrowsePrevACommand { get; }

    /// <summary>Deck A: load the next track from the browser's current list (load-or-queue).</summary>
    public ReactiveCommand<Unit, Unit> BrowseNextACommand { get; }

    /// <summary>Deck B: load the previous track from the browser's current list (load-or-queue).</summary>
    public ReactiveCommand<Unit, Unit> BrowsePrevBCommand { get; }

    /// <summary>Deck B: load the next track from the browser's current list (load-or-queue).</summary>
    public ReactiveCommand<Unit, Unit> BrowseNextBCommand { get; }

    /// <summary>The shared deck set (decks + mixer + waveform ZOOM), identical to the DJ and LIVE tabs.</summary>
    public PerformanceDeckSet Decks { get; }

    /// <summary>Deck A's external FX rack (Moog cutoff/res, Phaser, Reverb).</summary>
    public DeckFxRackViewModel FxRackA { get; }

    /// <summary>Deck B's external FX rack.</summary>
    public DeckFxRackViewModel FxRackB { get; }

    /// <summary>Deck A's per-stem level knobs.</summary>
    public DeckStemRackViewModel StemRackA { get; }

    /// <summary>Deck B's per-stem level knobs.</summary>
    public DeckStemRackViewModel StemRackB { get; }

    /// <summary>The shared DJ track browser (bottom half), or null when no catalog is wired.</summary>
    public DjBrowserViewModel? Browser { get; }

    /// <summary>True when a browser is available to show (drives the browser band's visibility).</summary>
    public bool HasBrowser => Browser is not null;

    // Only the stem racks hold a subscription (to DeckStemGain feedback for availability); the decks/mixer/
    // browser are shared instances owned elsewhere, and the FX racks only emit.
    public void Dispose()
    {
        StemRackA.Dispose();
        StemRackB.Dispose();
    }
}
