using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

/// <summary>
/// Verifies the shared performance modules holder (doc 11): it exposes the two decks + mixer, and — the
/// point of the type — when one instance is handed to both the DJ tab and the Live tab they drive the
/// SAME deck/mixer instances rather than look-alike copies (one source of truth, doc 12).
/// </summary>
public sealed class PerformanceDeckSetTests
{
    [Fact]
    public void ExposesTwoDecksAndMixer()
    {
        var decks = new PerformanceDeckSet();

        Assert.Equal("A", decks.DeckA.DeckId);
        Assert.Equal("B", decks.DeckB.DeckId);
        Assert.NotNull(decks.Mixer);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var decks = new PerformanceDeckSet();

        decks.Dispose();
        decks.Dispose(); // second call must be a safe no-op
    }

    [Fact]
    public void DjAndLive_DriveTheSameDeckInstances_WhenSharedSetInjected()
    {
        var shared = new PerformanceDeckSet();

        var dj = new DjViewModel(decks: shared);
        var live = new LiveViewModel(decks: shared);

        Assert.Same(shared.DeckA, dj.DeckA);
        Assert.Same(shared.DeckB, dj.DeckB);
        Assert.Same(shared.Mixer, dj.Mixer);

        Assert.Same(dj.DeckA, live.DeckA);
        Assert.Same(dj.DeckB, live.DeckB);
        Assert.Same(dj.Mixer, live.Mixer);
    }

    // --- Shared waveform ZOOM knob (one control zooms both decks in seconds → kicks stack on A and B) ---

    [Fact]
    public void WaveformZoom_Zero_SetsBothDecksToWholeTrackOverview()
    {
        using var decks = new PerformanceDeckSet();

        decks.WaveformZoom = 0.0;

        Assert.Equal(0.0, decks.DeckA.ZoomWindow, precision: 6);
        Assert.Equal(0.0, decks.DeckB.ZoomWindow, precision: 6);
    }

    [Fact]
    public void WaveformZoom_ZoomedIn_AppliesEquallyToBothDecks()
    {
        using var decks = new PerformanceDeckSet();

        decks.WaveformZoom = 1.0; // most zoomed in

        Assert.True(decks.DeckA.ZoomWindow > 0.0);                                  // no longer the overview
        Assert.Equal(decks.DeckA.ZoomWindow, decks.DeckB.ZoomWindow, precision: 6); // same time-scale on A and B
    }

    [Fact]
    public void SetWaveformZoom_TightestSeconds_PutsKnobAtMax()
    {
        using var decks = new PerformanceDeckSet();

        decks.SetWaveformZoom(VisualsSettings.MinZoomSeconds); // the most magnified window

        Assert.Equal(1.0, decks.WaveformZoom, precision: 6);
    }

    [Fact]
    public void SetWaveformZoom_WidestSeconds_PutsKnobAtZero()
    {
        using var decks = new PerformanceDeckSet();

        decks.SetWaveformZoom(VisualsSettings.MaxZoomSeconds); // the widest supported window

        Assert.Equal(0.0, decks.WaveformZoom, precision: 6);
    }

    [Fact]
    public void Ctor_SeedsTheKnobFromTheInitialZoomSeconds()
    {
        using var decks = new PerformanceDeckSet(waveformZoomSeconds: VisualsSettings.MinZoomSeconds);

        Assert.Equal(1.0, decks.WaveformZoom, precision: 6);
    }
}
