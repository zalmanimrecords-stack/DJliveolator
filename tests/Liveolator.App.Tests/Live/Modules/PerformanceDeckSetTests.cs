using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Live.Modules;
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
}
