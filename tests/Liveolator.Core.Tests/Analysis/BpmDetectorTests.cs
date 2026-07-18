using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class BpmDetectorTests
{
    [Theory]
    [InlineData(120.0)]
    [InlineData(128.0)]
    [InlineData(90.0)]
    public void Detect_ClickTrain_RecoversTempo(double bpm)
    {
        const int sr = 44100;
        float[] signal = TestSignals.ClickTrain(bpm, sr, seconds: 10);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0);
        Assert.True(result.Confidence > 0, "a clear click train should yield non-zero confidence");
    }

    [Fact]
    public void Detect_AccentedFastTrack_DoesNotCollapseToHalfTempo()
    {
        const int sr = 44100;
        float[] signal = AccentedClickTrain(
            bpm: 150.0, sampleRate: sr, seconds: 12, strong: 1.0f, weak: 0.35f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.Bpm, 147.0, 153.0);
    }

    [Fact]
    public void Detect_ClickOnTheBeat_AnchorsFirstBeatNearZero()
    {
        const int sr = 44100;
        float[] signal = TestSignals.ClickTrain(120.0, sr, seconds: 10);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        // Clicks start at t=0, so the first-beat anchor lands on the beat. A beat phase is CIRCULAR (the
        // kick-band anchor can report a hair before zero, i.e. just under one beat), so assert circular
        // proximity to 0 rather than a one-sided sub-range — both are the same grid for sync (BeatDistance).
        double period = 60.0 / result.Bpm;
        Assert.True(
            BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, 0.0, period) < 0.03,
            $"first beat should sit on the beat; was {result.FirstBeatSeconds:F4}s (period {period:F4}s)");
    }

    [Fact]
    public void Detect_OffsetClickTrain_RecoversTheFirstBeatOffset()
    {
        const int sr = 44100;
        // First click 0.12 s in; the anchor is a within-beat offset, so it should report ~0.12 s.
        float[] signal = TestSignals.ClickTrain(120.0, sr, seconds: 10, offsetSeconds: 0.12);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.FirstBeatSeconds, 0.10, 0.14);
        Assert.InRange(result.FirstBeatSeconds, 0.0, 60.0 / result.Bpm); // within one beat
    }

    [Fact]
    public void Detect_KickAccentedOnDownbeat_AnchorsDownbeatThere()
    {
        const int sr = 44100;
        // Four-on-the-floor at 120 BPM with a louder kick on beat 1 of each bar: the downbeat anchor
        // should land near t=0 and report real (non-zero) confidence.
        float[] signal = KickFourOnFloor(
            bpm: 120.0, sampleRate: sr, seconds: 16, accentBeat: 0, strong: 1.0f, weak: 0.4f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.Equal(4, result.BeatsPerBar);
        // The downbeat is a CIRCULAR bar-phase anchor; the kick-band fit can land a hair before zero (i.e.
        // just under one bar), so assert circular proximity to the bar origin rather than a one-sided range.
        double barSeconds = result.BeatsPerBar * 60.0 / result.Bpm;
        Assert.True(
            BeatMixSignals.CircularDistanceSeconds(result.DownbeatSeconds, 0.0, barSeconds) < 0.06,
            $"downbeat should sit on beat 1; was {result.DownbeatSeconds:F4}s (bar {barSeconds:F4}s)");
        Assert.True(result.DownbeatConfidence > 0.1, $"accented downbeat should be confident, was {result.DownbeatConfidence:F3}");
    }

    [Fact]
    public void Detect_FourOnFloor_RefinesToCleanIntegerTempo()
    {
        const int sr = 44100;
        // A clean 140 BPM four-on-the-floor. Integer-lag autocorrelation lands on 139.67 (lag 37); the
        // grid refiner must pull it back to a clean 140 so a uniform grid stays on the kicks for the whole
        // track. This is the regression guard for the "kick and grid not in the same place" bug.
        float[] signal = KickFourOnFloor(
            bpm: 140.0, sampleRate: sr, seconds: 30, accentBeat: 0, strong: 1.0f, weak: 1.0f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.Bpm, 139.9, 140.1);
    }

    [Fact]
    public void Detect_TooShortSignal_ReturnsZero()
    {
        var tiny = new float[16];
        BpmResult result = new BpmDetector().Detect(tiny, 44100);
        Assert.Equal(0, result.Bpm);
        Assert.Equal(0, result.FirstBeatSeconds);
    }

    [Fact]
    public void Detect_CleanGrid_PopulatesGridConfidenceSignals_AndOffersPhaseSync()
    {
        const int sr = 44100;
        // A clean, constant 140 BPM four-on-the-floor: the kicks sit tightly on the grid and the tempo does
        // not drift, so both grid-confidence signals should be present and strong (SYNC-BEHAVIOR-SPEC §7).
        float[] signal = KickFourOnFloor(
            bpm: 140.0, sampleRate: sr, seconds: 30, accentBeat: 0, strong: 1.0f, weak: 1.0f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.NotNull(result.GridCoherence);
        Assert.True(result.GridCoherence > 0.5, $"clean kicks should cohere, was {result.GridCoherence:F3}");
        Assert.NotNull(result.TempoStabilityBpmDelta);
        Assert.True(result.TempoStabilityBpmDelta < 0.75, $"constant tempo should be stable, delta was {result.TempoStabilityBpmDelta:F3}");
        Assert.True(GridConfidenceCalculator.Evaluate(result).PhaseSyncReady);
    }

    [Fact]
    public void Detect_VariableTempo_ReportsLargeStabilityDelta_AndDowngrades()
    {
        const int sr = 44100;
        // First half at 120, second half at 140: a genuinely variable-tempo track. The half-vs-half tempo
        // delta must be large so the gate downgrades to tempo-only rather than phase-locking a drifting grid.
        float[] signal = Concat(
            TestSignals.ClickTrain(120.0, sr, seconds: 12),
            TestSignals.ClickTrain(140.0, sr, seconds: 12));

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.NotNull(result.TempoStabilityBpmDelta);
        Assert.True(result.TempoStabilityBpmDelta > 2.0, $"a 120→140 track should read as unstable, delta was {result.TempoStabilityBpmDelta:F3}");
        Assert.False(GridConfidenceCalculator.Evaluate(result).PhaseSyncReady);
    }

    [Fact]
    public void BpmResult_DefaultsGridSignalsToNull_ForOldCatalogBackCompat()
    {
        // A track deserialized from a pre-v9 catalog has no grid signals: they default to null so the gate
        // treats it as Unknown and preserves phase sync until the background re-analysis populates them.
        var legacy = new BpmResult(128.0, 0.9, 0.0);

        Assert.Null(legacy.GridCoherence);
        Assert.Null(legacy.TempoStabilityBpmDelta);
        Assert.False(GridConfidenceCalculator.Evaluate(legacy).Analyzed);
    }

    private static float[] Concat(float[] a, float[] b)
    {
        var result = new float[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    [Fact]
    public void Detect_InvalidSampleRate_Throws()
    {
        var buffer = new float[2048];
        Assert.Throws<ArgumentOutOfRangeException>(() => new BpmDetector().Detect(buffer, 0));
    }

    /// <summary>
    /// A four-on-the-floor low-frequency kick pattern: a 55 Hz tone burst on every beat, louder on the
    /// bar-relative <paramref name="accentBeat"/>, so the downbeat is recoverable from the kick band.
    /// </summary>
    private static float[] KickFourOnFloor(
        double bpm, int sampleRate, double seconds, int accentBeat, float strong, float weak)
    {
        const int beatsPerBar = 4;
        var signal = new float[(int)(sampleRate * seconds)];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        int burstLen = (int)(0.05 * sampleRate);
        double w = 2.0 * Math.PI * 55.0 / sampleRate;
        for (double position = 0, beat = 0; position < signal.Length; position += samplesPerBeat, beat++)
        {
            float amplitude = (int)beat % beatsPerBar == accentBeat ? strong : weak;
            int start = (int)position;
            for (int sample = 0; sample < burstLen && start + sample < signal.Length; sample++)
                signal[start + sample] = (float)(amplitude * Math.Sin(w * sample));
        }
        return signal;
    }

    private static float[] AccentedClickTrain(
        double bpm,
        int sampleRate,
        double seconds,
        float strong,
        float weak)
    {
        var signal = new float[(int)(sampleRate * seconds)];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        for (double position = 0, beat = 0; position < signal.Length; position += samplesPerBeat, beat++)
        {
            float amplitude = ((int)beat & 1) == 0 ? strong : weak;
            int start = (int)position;
            for (int sample = 0; sample < 16 && start + sample < signal.Length; sample++)
                signal[start + sample] = amplitude;
        }
        return signal;
    }
}
