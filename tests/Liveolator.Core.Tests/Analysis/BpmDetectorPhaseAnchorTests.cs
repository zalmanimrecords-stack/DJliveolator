using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// Phase-anchor regression: the first-beat anchor that two-deck sync aligns to must follow the KICK, not
/// whatever broadband transient is loudest. On a four-on-the-floor with a sustained off-beat bass and a
/// bright off-beat hat, a broadband-energy anchor is pulled toward the off-beat; the kick-band anchor stays
/// on the down-beat. See docs/24+ system review (2026-06-27) — anchoring phase on the broadband envelope
/// (BpmDetector) was the dominant cause of unsatisfying beat-sync.
/// </summary>
public sealed class BpmDetectorPhaseAnchorTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void Detect_KickWithOffbeatBroadbandPollution_AnchorsPhaseToTheKick()
    {
        // The off-beat pollution (bass + hats) sits ABOVE the kick crossover, so the kick band stays clean
        // and the only failure mode is the phase SOURCE: a broadband anchor follows the loud off-beat
        // transients, a kick anchor stays on the down-beat. This isolates the P0 phase-source bug.
        const double bpm = 128.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            bpm, SampleRate, seconds: 20.0, kickOffsetSeconds: kickOffsetSeconds, bassHz: 320.0);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        // Guard: the test must fail on PHASE, not because tempo detection collapsed.
        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0);

        double period = 60.0 / result.Bpm;
        double offbeatSeconds = kickOffsetSeconds + period / 2.0;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, kickOffsetSeconds, period);
        double toOffbeat = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, offbeatSeconds, period);

        Assert.True(
            toKick < 0.03,
            $"first-beat anchor should sit on the kick (offset {kickOffsetSeconds:F3}s); " +
            $"was {result.FirstBeatSeconds:F3}s, distance-to-kick {toKick:F3}s, distance-to-offbeat {toOffbeat:F3}s");
        Assert.True(toKick < toOffbeat, "anchor must be closer to the kick than to the off-beat");
    }

    [Fact(Skip = "P1 (HPSS): a sustained off-beat bass INSIDE the kick band corrupts the kick onset " +
                 "envelope itself (coherence collapses), so the phase-source fix alone cannot anchor to the " +
                 "kick. Requires percussive/harmonic separation before the onset stage. Tracked in the " +
                 "beat-sync-stems plan.")]
    public void Detect_KickWithOffbeatSubBassInKickBand_AnchorsPhaseToTheKick()
    {
        const double bpm = 128.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            bpm, SampleRate, seconds: 20.0, kickOffsetSeconds: kickOffsetSeconds, bassHz: 110.0);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        double period = 60.0 / result.Bpm;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, kickOffsetSeconds, period);
        Assert.True(toKick < 0.03, $"anchor distance-to-kick {toKick:F3}s");
    }
}
