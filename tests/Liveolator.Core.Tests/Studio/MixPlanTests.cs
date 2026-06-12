using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class MixPlanTests
{
    private const double Tol = 1e-9;

    private static StudioClip Clip(int deck, double start, double sourceInSec, double? lengthSec)
        => new(deck, $"/m/d{deck}.wav", start, TimeSpan.FromSeconds(sourceInSec),
            lengthSec is { } l ? TimeSpan.FromSeconds(sourceInSec + l) : null);

    [Fact]
    public void EvaluateDeck_NoClip_IsSilentWithNeutralControls()
    {
        var plan = new MixPlan(StudioProject.Empty("p"));

        DeckMixState s = plan.EvaluateDeck(0, 5);

        Assert.False(s.HasAudio);
        Assert.Equal(1.0, s.Gain, Tol);          // default unity gain
        Assert.Equal(EqBands.Flat, s.Eq);
        Assert.Equal(DeckChannelState.FilterCenter, s.Filter, Tol);
    }

    [Fact]
    public void EvaluateDeck_InsideClip_MapsTimelineToSource()
    {
        var p = new StudioProject("p", 120,
            new[] { Clip(2, start: 10, sourceInSec: 30, lengthSec: 60) },
            Array.Empty<AutomationLane>());
        var plan = new MixPlan(p);

        DeckMixState s = plan.EvaluateDeck(2, timeSeconds: 25); // 15s into the clip

        Assert.True(s.HasAudio);
        Assert.Equal(45, s.SourceSeconds, Tol); // sourceIn 30 + 15
    }

    [Fact]
    public void EvaluateDeck_BeforeAndAfterClip_HasNoAudio()
    {
        var p = new StudioProject("p", 120,
            new[] { Clip(0, start: 10, sourceInSec: 0, lengthSec: 5) }, // sounds [10,15)
            Array.Empty<AutomationLane>());
        var plan = new MixPlan(p);

        Assert.False(plan.EvaluateDeck(0, 9).HasAudio);
        Assert.True(plan.EvaluateDeck(0, 10).HasAudio);
        Assert.False(plan.EvaluateDeck(0, 15).HasAudio); // half-open end
    }

    [Fact]
    public void EvaluateDeck_AppliesAutomationValues()
    {
        var p = new StudioProject("p", 120,
            new[] { Clip(1, start: 0, sourceInSec: 0, lengthSec: 100) },
            new[]
            {
                new AutomationLane(AutomationTarget.DeckGain, 1, new[]
                {
                    new AutomationKeyframe(0, 0.0), new AutomationKeyframe(10, 1.0),
                }),
                new AutomationLane(AutomationTarget.EqLow, 1, new[] { new AutomationKeyframe(0, 0.0) }),
            });
        var plan = new MixPlan(p);

        DeckMixState s = plan.EvaluateDeck(1, 5); // gain ramp halfway

        Assert.Equal(0.5, s.Gain, Tol);
        Assert.Equal(0.0, s.Eq.Low, Tol); // EQ low killed
        Assert.Equal(EqBands.Unity, s.Eq.Mid, Tol); // untouched band stays flat
    }

    [Fact]
    public void EvaluateDeck_OverlappingClips_LatestStartedWins()
    {
        var p = new StudioProject("p", 120, new[]
        {
            Clip(0, start: 0, sourceInSec: 0, lengthSec: 100),
            Clip(0, start: 20, sourceInSec: 0, lengthSec: 100),
        }, Array.Empty<AutomationLane>());
        var plan = new MixPlan(p);

        DeckMixState s = plan.EvaluateDeck(0, 25); // both cover 25; the one starting at 20 wins
        Assert.Equal(5, s.SourceSeconds, Tol);     // 25 - 20
    }
}
