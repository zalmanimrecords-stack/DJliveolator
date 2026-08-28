using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio.Set;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// The one energy question the planner and the export gate both ask: is anything driving the floor here?
/// Answered bar by bar off the already-persisted kick onsets, so an un-analyzed record must read as
/// unknown rather than as silence.
/// </summary>
public class KickCoverageTests
{
    private const double BarSeconds = 1.875;    // 128 BPM
    private const double BeatSeconds = 0.46875;

    private static double[] KicksEveryBeat(double fromSeconds, int count)
        => Enumerable.Range(0, count).Select(i => fromSeconds + (i * BeatSeconds)).ToArray();

    [Fact]
    public void Coverage_IsOne_WhenEveryBarHasAKick()
    {
        MusicTrack track = SetTrackFixture.Track("a.mp3", kicks: KicksEveryBeat(0.0, 128));

        Assert.Equal(1.0, KickCoverage.Fraction(track, startSeconds: 0.0, bars: 16));
    }

    [Fact]
    public void Coverage_IsZero_AcrossASilentBreakdown()
    {
        // Drums for the first 30 s only; the window sits entirely in the breakdown that follows.
        MusicTrack track = SetTrackFixture.Track("a.mp3", kicks: KicksEveryBeat(0.0, 64));

        Assert.Equal(0.0, KickCoverage.Fraction(track, startSeconds: 60.0, bars: 8));
    }

    [Fact]
    public void Coverage_CountsABarAsCovered_FromASingleStrike()
    {
        // Bars 0 and 2 have one kick each, bars 1 and 3 none.
        MusicTrack track = SetTrackFixture.Track("a.mp3", kicks: new[] { 0.1, 2.0 * BarSeconds });

        Assert.Equal(0.5, KickCoverage.Fraction(track, startSeconds: 0.0, bars: 4));
    }

    [Fact]
    public void LongestJointKicklessRun_CountsBarsWhereNeitherSideHasAKick()
    {
        // Outgoing: kicks through bars 0-1, then nothing. Incoming: nothing until its bar 4.
        // Bars 2 and 3 are empty on both decks — the hole a listener hears.
        MusicTrack outgoing = SetTrackFixture.Track("out.mp3", kicks: KicksEveryBeat(0.0, 8));
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", kicks: KicksEveryBeat(100.0 + (4 * BarSeconds), 32));

        int? run = KickCoverage.LongestJointKicklessRun(
            outgoing, outgoingStartSeconds: 0.0, incoming, incomingStartSeconds: 100.0, bars: 8);

        Assert.Equal(2, run);
    }

    [Fact]
    public void LongestJointKicklessRun_IsZero_WhenOneDeckAlwaysHasAKick()
    {
        MusicTrack outgoing = SetTrackFixture.Track("out.mp3", kicks: KicksEveryBeat(0.0, 128));
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", kicks: new[] { 0.0 });

        int? run = KickCoverage.LongestJointKicklessRun(
            outgoing, outgoingStartSeconds: 0.0, incoming, incomingStartSeconds: 0.0, bars: 16);

        Assert.Equal(0, run);
    }

    [Fact]
    public void Coverage_ReportsUnknown_WhenKickOnsetsWereNeverAnalyzed()
    {
        // A record nobody measured must NOT read as "no kicks", or every un-analyzed track is unmixable.
        MusicTrack unmeasured = SetTrackFixture.Track("a.mp3");
        MusicTrack measured = SetTrackFixture.Track("b.mp3", kicks: KicksEveryBeat(0.0, 128));

        Assert.Null(KickCoverage.Fraction(unmeasured, startSeconds: 0.0, bars: 16));
        Assert.Null(KickCoverage.LongestJointKicklessRun(measured, 0.0, unmeasured, 0.0, bars: 16));
        Assert.Null(KickCoverage.LongestJointKicklessRun(unmeasured, 0.0, measured, 0.0, bars: 16));
    }

    [Fact]
    public void Coverage_ReportsUnknown_WhenTheTrackHasNoTempoToMeasureBarsAgainst()
    {
        MusicTrack noTempo = SetTrackFixture.Track("a.mp3", bpm: 0.0, kicks: KicksEveryBeat(0.0, 16));

        Assert.Null(KickCoverage.Fraction(noTempo, startSeconds: 0.0, bars: 16));
    }
}
