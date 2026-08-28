using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio.Set;
using Xunit;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// The measured condition a mix point has to satisfy: something must be driving the floor at both ends of
/// the blend. The 2026-08-13 set is why — join 1 opened a 10.5 s hole bottoming at -63.7 dB because both
/// records were withdrawing at once, and the planner reported that join as its most trusted kind.
/// <para>The gate REJECTS rather than warns on the outgoing side, and a track whose kicks were never
/// analyzed must plan exactly as it did before the gate existed.</para>
/// </summary>
public class SetTransitionPlannerEnergyTests
{
    private static readonly SetBuildOptions Options = new();

    private static List<SetWarning> NoWarnings() => new();

    private static MixAnchor At(double seconds) => new(seconds, null, AnchorSource.Fallback);

    [Fact]
    public void MixOut_RejectsAnAnchorBelowTheCoverageFloor()
    {
        MusicTrack driven = EnergyTrackFixture.Track("driven.mp3", EnergyTrackFixture.Beats(0.0, 300.0));
        MusicTrack withdrawn = EnergyTrackFixture.Track("withdrawn.mp3", EnergyTrackFixture.Beats(0.0, 200.0));
        MusicTrack incoming = EnergyTrackFixture.Track("in.mp3", EnergyTrackFixture.Beats(0.0, 300.0));

        Assert.True(
            SetTransitionPlanner.KeepsTheFloorMoving(driven, At(240.0), incoming, At(0.0), 16, NoWarnings()),
            "a fully driven mix-out window must be accepted");
        Assert.False(
            SetTransitionPlanner.KeepsTheFloorMoving(withdrawn, At(240.0), incoming, At(0.0), 16, NoWarnings()),
            "leaving a record from a window with no kicks in it records its own hole into the mix");
    }

    [Fact]
    public void MixIn_WarnsButDoesNotReject_BelowTheLenientFloor()
    {
        // Entering on a rising intro is normal practice, so the incoming floor reports rather than refuses.
        MusicTrack outgoing = EnergyTrackFixture.Track("out.mp3", EnergyTrackFixture.Beats(0.0, 300.0));
        double[] everyOtherBar = Enumerable.Range(0, 8).Select(k => k * 2 * EnergyTrackFixture.BarSeconds).ToArray();
        MusicTrack sparse = EnergyTrackFixture.Track("sparse.mp3", everyOtherBar);
        var warnings = NoWarnings();

        bool accepted = SetTransitionPlanner.KeepsTheFloorMoving(outgoing, At(240.0), sparse, At(0.0), 16, warnings);

        Assert.True(accepted);
        Assert.Contains(SetWarning.LowKickCoverageAtMixIn, warnings);
    }

    [Fact]
    public void Join_IsRejected_WhenNeitherDeckHasAKickForTwoBars()
    {
        // Both sides stay above their own floors here: the hole is only two bars of a 24-bar blend. It is the
        // coincidence that empties the floor, which is why the rule is joint (owner decision, 2026-08-28).
        MusicTrack TwoBarHole(string path, double origin) => EnergyTrackFixture.Track(
            path, EnergyTrackFixture.Beats(0.0, 300.0, EnergyTrackFixture.Hole(origin, fromBar: 10, bars: 2)));
        MusicTrack OneBarHole(string path, double origin) => EnergyTrackFixture.Track(
            path, EnergyTrackFixture.Beats(0.0, 300.0, EnergyTrackFixture.Hole(origin, fromBar: 10, bars: 1)));

        Assert.False(
            SetTransitionPlanner.KeepsTheFloorMoving(
                TwoBarHole("out.mp3", 240.0), At(240.0), TwoBarHole("in.mp3", 0.0), At(0.0), 24, NoWarnings()),
            "two bars with no kick on either deck is a hole a listener hears");
        Assert.True(
            SetTransitionPlanner.KeepsTheFloorMoving(
                OneBarHole("out.mp3", 240.0), At(240.0), OneBarHole("in.mp3", 0.0), At(0.0), 24, NoWarnings()),
            "one bar is the most that is allowed, so it must still pass");
    }

    [Fact]
    public void Join_PlansExactlyAsBefore_WhenKickOnsetsAreMissing()
    {
        // The backward-compatibility guard: UNKNOWN must never be read as "no kicks", or every record the
        // catalog never analyzed for kicks becomes unmixable.
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track("b.mp3", structure: SetTrackFixture.StandardStructure());

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(16, shape!.OverlapBars);
        Assert.Equal(240.0, shape.Out.SourceSeconds, 3);
        Assert.Equal(0.0, shape.In.SourceSeconds, 3);
        Assert.DoesNotContain(SetWarning.LowKickCoverageAtMixIn, shape.Warnings);
    }

    [Fact]
    public void Plan_RefusesAJoin_WhoseOnlyMixOutSitsInAKicklessTail()
    {
        // End to end: every blend length the planner can step down to lands in the same withdrawn tail, so
        // there is no legal join into this record and the arranger drops it rather than mixing badly.
        MusicTrack withdrawn = EnergyTrackFixture.Track("withdrawn.mp3", EnergyTrackFixture.Beats(0.0, 200.0));
        MusicTrack driven = EnergyTrackFixture.Track("driven.mp3", EnergyTrackFixture.Beats(0.0, 300.0));
        MusicTrack to = EnergyTrackFixture.Track("to.mp3", EnergyTrackFixture.Beats(0.0, 300.0));

        Assert.Null(SetTransitionPlanner.Plan(withdrawn, 0.0, to, Options, true, true));
        Assert.NotNull(SetTransitionPlanner.Plan(driven, 0.0, to, Options, true, true));
    }
}
