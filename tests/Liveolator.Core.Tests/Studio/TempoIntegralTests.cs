using Liveolator.Core.Studio;

namespace Liveolator.Core.Tests.Studio;

/// <summary>
/// The geometry a ramped set tempo needs. With one flat tempo a clip's source and timeline are related by
/// a single factor and a division answers everything; once the tempo moves, the source a clip consumes is
/// the INTEGRAL of the tempo over the span, and placing the next clip needs that integral inverted.
/// </summary>
public sealed class TempoIntegralTests
{
    private const double Bpm = 140.0;

    private static TempoCurve Ramp(double fromBpm, double toBpm, double overSeconds)
        => new(new[] { new TempoKeyframe(0.0, fromBpm), new TempoKeyframe(overSeconds, toBpm) });

    [Fact]
    public void SourceSeconds_MatchesTheFlatFactor_WhenTheTempoDoesNotMove()
    {
        // The flat case must stay exactly what it was, because every existing set was placed with it.
        double source = TempoIntegral.SourceSeconds(TempoCurve.Empty, Bpm, sourceBpm: 134.0, 10.0, 70.0);

        Assert.Equal(60.0 * (140.0 / 134.0), source, 9);
    }

    [Fact]
    public void SourceSeconds_UsesTheMeanTempo_AcrossALinearRamp()
    {
        // A linear ramp's integral is its trapezoid: 60 s spent going 130 -> 150 consumes exactly as much
        // source as 60 s at a flat 140. Exact, not approximate — the curve is linear by definition.
        double ramped = TempoIntegral.SourceSeconds(Ramp(130.0, 150.0, 60.0), Bpm, 140.0, 0.0, 60.0);
        double flat = TempoIntegral.SourceSeconds(TempoCurve.Empty, Bpm, 140.0, 0.0, 60.0);

        Assert.Equal(flat, ramped, 9);
    }

    [Fact]
    public void SourceSeconds_HoldsTheEndValue_OutsideTheKeyframes()
    {
        // The curve is flat before the first keyframe and after the last, and a set's ramp does not cover
        // the head and tail of the timeline — so the clips out there must still be placed.
        double after = TempoIntegral.SourceSeconds(Ramp(130.0, 150.0, 60.0), Bpm, 150.0, 60.0, 120.0);

        Assert.Equal(60.0, after, 9);   // 150 BPM source played at 150 BPM: 1:1
    }

    [Fact]
    public void TimelineSecondsForSource_InvertsTheIntegral()
    {
        // The property that matters: consuming the source the forward pass reports must land back on the
        // same timeline instant. Asserted across a ramp, where a division would be wrong.
        TempoCurve curve = Ramp(130.0, 150.0, 90.0);
        double source = TempoIntegral.SourceSeconds(curve, Bpm, 134.0, 12.0, 74.0);

        double back = TempoIntegral.TimelineSecondsForSource(curve, Bpm, 134.0, 12.0, source);

        Assert.Equal(74.0, back, 6);
    }

    [Fact]
    public void TimelineSecondsForSource_InvertsAcrossSeveralSegments()
    {
        // A set's curve is flat over each blend and ramps between, so the span a single clip covers
        // routinely crosses three segments. Solving only within one was the obvious way to get this wrong.
        var curve = new TempoCurve(new[]
        {
            new TempoKeyframe(0.0, 134.0),
            new TempoKeyframe(30.0, 134.0),
            new TempoKeyframe(200.0, 142.0),
            new TempoKeyframe(260.0, 142.0),
        });
        double source = TempoIntegral.SourceSeconds(curve, Bpm, 138.0, 10.0, 250.0);

        double back = TempoIntegral.TimelineSecondsForSource(curve, Bpm, 138.0, 10.0, source);

        Assert.Equal(250.0, back, 6);
    }

    [Fact]
    public void TimelineSecondsForSource_ReturnsTheStart_ForNoSource()
    {
        double t = TempoIntegral.TimelineSecondsForSource(Ramp(130.0, 150.0, 60.0), Bpm, 134.0, 17.5, 0.0);

        Assert.Equal(17.5, t, 9);
    }

    [Fact]
    public void WarpedTimelineWidth_FollowsTheCurve_NotTheTempoAtTheClipStart()
    {
        // The constant-per-clip model reads the tempo once, at the clip's start. Under a ramp that is the
        // tempo the clip has ALREADY left: measured on a set where a record entered at 139 and spent most of
        // its life at 137, the width came out 4.2 s short, which ate a third of the blend that followed and
        // had the export gate report a 14 s mix as a 9.8 s cut.
        var curve = new TempoCurve(new[]
        {
            new TempoKeyframe(0.0, 139.0),
            new TempoKeyframe(25.0, 139.0),
            new TempoKeyframe(85.0, 137.0),
        });
        var clip = new StudioClip(
            DeckSlot: 0, TrackPath: "a.mp3", TimelineStartSeconds: 0.0,
            SourceIn: TimeSpan.Zero, SourceOut: TimeSpan.FromSeconds(342.857),
            SourceBpm: 140.0, WarpEnabled: true);

        double width = WarpMath.WarpedTimelineWidth(clip, curve, defaultBpm: 140.0);
        double bySlowerTail = TempoIntegral.TimelineSecondsForSource(curve, 140.0, 140.0, 0.0, 342.857);

        Assert.Equal(bySlowerTail, width, 6);
        // The clip runs mostly at 137 against its own 140, so it occupies MORE timeline than the 139 at its
        // start implies. Being short here is what truncates the record before its blend is done.
        Assert.True(width > 342.857 * (140.0 / 139.0), $"width {width:F2}s still reads the start tempo");
    }

    [Fact]
    public void SourceSeconds_IsZero_ForAnEmptySpan()
        => Assert.Equal(0.0, TempoIntegral.SourceSeconds(Ramp(130.0, 150.0, 60.0), Bpm, 134.0, 20.0, 20.0), 12);

    [Fact]
    public void SourceSeconds_RefusesANonPositiveSourceTempo()
    {
        // A track with no analyzed tempo cannot be warped onto a curve, and returning 0 here would silently
        // place it at the previous clip's start.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TempoIntegral.SourceSeconds(TempoCurve.Empty, Bpm, sourceBpm: 0.0, 0.0, 10.0));
    }
}
