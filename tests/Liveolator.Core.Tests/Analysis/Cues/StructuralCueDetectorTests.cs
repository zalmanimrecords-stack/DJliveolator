using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class StructuralCueDetectorTests
{
    private const int Sr = 44100;
    private const double Bpm = 120.0;

    // 2-bar phrases keep the synthetic track short while still exercising phrase quantization
    // (at 120 BPM a 2-bar phrase is ~4 s, so sections are built on 4 s multiples).
    private static StructuralCueDetector Detector() => new(phraseBars: 2);

    [Fact]
    public void Detect_EmptyBands_ReturnsEmpty()
    {
        StructuralCueResult result = Detector().Detect(
            BandEnergyFrames.Empty, new BpmResult(Bpm, 0.9), TrackCues.None, durationSeconds: 0);

        Assert.Equal(StructuralCueResult.Empty, result);
    }

    [Fact]
    public void Detect_UndetectableTempo_ReturnsEmpty()
    {
        var signal = LowTone(seconds: 10);
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);

        StructuralCueResult result = Detector().Detect(
            bands, new BpmResult(Bpm: 0, Confidence: 0), TrackCues.None, durationSeconds: 10);

        Assert.Equal(StructuralCueResult.Empty, result);
    }

    [Fact]
    public void Detect_StructuredTrack_FindsDropBreakdownAndBuildInOrder()
    {
        var (signal, duration) = StructuredTrack();
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);
        TrackCues silence = new SilenceCueDetector().Detect(signal, Sr);

        StructuralCueResult result = Detector().Detect(bands, new BpmResult(Bpm, 0.9), silence, duration);

        double start = PositionOf(result, StructuralCueKind.TrackStart);
        double drop = PositionOf(result, StructuralCueKind.Drop);
        double breakdown = PositionOf(result, StructuralCueKind.Breakdown);
        double build = PositionOf(result, StructuralCueKind.BuildUp);

        Assert.InRange(start, 1.0, 3.0);     // intro begins ~2 s
        Assert.InRange(drop, 8.0, 12.0);     // kick enters ~10 s
        Assert.InRange(breakdown, 24.0, 28.0); // kick drops out ~26 s
        Assert.InRange(build, 32.0, 36.0);   // riser before second drop ~34 s
        Assert.True(start < drop && drop < breakdown && breakdown < build,
            $"cues out of order: start={start} drop={drop} breakdown={breakdown} build={build}");
        Assert.True(result.OverallConfidence >= 0.5);
    }

    [Fact]
    public void Detect_StructuredTrack_PlacesOutroAfterBreakdown()
    {
        var (signal, duration) = StructuredTrack();
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);
        TrackCues silence = new SilenceCueDetector().Detect(signal, Sr);

        StructuralCueResult result = Detector().Detect(bands, new BpmResult(Bpm, 0.9), silence, duration);

        double breakdown = PositionOf(result, StructuralCueKind.Breakdown);
        double outro = PositionOf(result, StructuralCueKind.OutroStart);
        Assert.True(outro > breakdown, $"outro {outro} should follow breakdown {breakdown}");
    }

    [Fact]
    public void Detect_TrustedDownbeat_AnchorsPhraseGridToDownbeat()
    {
        var (signal, duration) = StructuredTrack();
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);
        TrackCues silence = new SilenceCueDetector().Detect(signal, Sr);

        // A trusted downbeat half a bar off the intro edge (0.5 s within the 2 s bar at 120 BPM). The drop
        // cue must snap to a bar line sharing that phase, not to the RMS-intro-anchored grid (phase 0).
        var bpm = new BpmResult(Bpm, 0.9, FirstBeatSeconds: 0.5)
        {
            DownbeatSeconds = 0.5,
            DownbeatConfidence = 0.9,
        };

        StructuralCueResult result = Detector().Detect(bands, bpm, silence, duration);

        double drop = PositionOf(result, StructuralCueKind.Drop);
        double barSeconds = 4.0 * 60.0 / Bpm; // 2.0 s at 120 BPM / 4-4
        double phase = ((drop % barSeconds) + barSeconds) % barSeconds;
        Assert.InRange(phase, 0.4, 0.6); // aligned to the downbeat's 0.5 s bar phase, not 0
    }

    [Fact]
    public void Detect_FlatTrack_PlacesOnlySafeCuePair()
    {
        // Constant kick throughout: no readable structure -> conservative behaviour.
        var signal = LowTone(seconds: 40);
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);
        TrackCues silence = new SilenceCueDetector().Detect(signal, Sr);

        StructuralCueResult result = Detector().Detect(bands, new BpmResult(Bpm, 0.9), silence, 40);

        AssertOnlySafePair(result);
    }

    [Fact]
    public void Detect_LowTempoConfidence_PlacesOnlySafeCuePair()
    {
        var (signal, duration) = StructuredTrack();
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(signal, Sr);
        TrackCues silence = new SilenceCueDetector().Detect(signal, Sr);

        // Even on a clearly structured track, low tempo confidence suppresses speculative cues.
        StructuralCueResult result = Detector().Detect(bands, new BpmResult(Bpm, Confidence: 0.2), silence, duration);

        AssertOnlySafePair(result);
        Assert.True(result.OverallConfidence < 0.5);
    }

    private static void AssertOnlySafePair(StructuralCueResult result)
    {
        IEnumerable<StructuralCueKind> kinds = result.Cues.Select(c => c.Kind);
        Assert.Contains(StructuralCueKind.TrackStart, kinds);
        Assert.Contains(StructuralCueKind.OutroStart, kinds);
        Assert.DoesNotContain(StructuralCueKind.Drop, kinds);
        Assert.DoesNotContain(StructuralCueKind.Breakdown, kinds);
        Assert.DoesNotContain(StructuralCueKind.BuildUp, kinds);
    }

    private static double PositionOf(StructuralCueResult result, StructuralCueKind kind)
    {
        StructuralCue cue = result.Cues.SingleOrDefault(c => c.Kind == kind);
        Assert.True(cue.Kind == kind, $"expected a {kind} cue, found none");
        return cue.PositionSeconds;
    }

    /// <summary>
    /// A synthetic EDM-shaped track: silence → melodic intro → drop → melodic breakdown → riser →
    /// second drop → silence. Section boundaries land on 4 s (2-bar) multiples so they align to the
    /// detector's phrase grid.
    /// </summary>
    private static (float[] signal, double duration) StructuredTrack()
    {
        var parts = new List<float[]>
        {
            Silence(2),         // 0–2   lead-in
            MidTone(8),         // 2–10  intro (audible, no kick)
            LowTone(16),        // 10–26 drop
            MidTone(8),         // 26–34 breakdown (melodic, no kick)
            HighRamp(4),        // 34–38 build-up (rising highs, no kick)
            LowTone(8),         // 38–46 second drop
            Silence(2),         // 46–48 outro
        };
        var signal = parts.SelectMany(p => p).ToArray();
        return (signal, (double)signal.Length / Sr);
    }

    private static float[] Silence(double seconds) => new float[(int)(Sr * seconds)];

    private static float[] LowTone(double seconds) => TestSignals.Sine(60, Sr, seconds, amplitude: 0.9);

    private static float[] MidTone(double seconds) => TestSignals.Sine(1000, Sr, seconds, amplitude: 0.7);

    /// <summary>A high-band tone whose amplitude ramps up — a build-up riser.</summary>
    private static float[] HighRamp(double seconds)
    {
        int total = (int)(Sr * seconds);
        var buffer = new float[total];
        double w = 2.0 * Math.PI * 6000 / Sr;
        for (int i = 0; i < total; i++)
        {
            double amp = 0.2 + 0.7 * ((double)i / total);
            buffer[i] = (float)(amp * Math.Sin(w * i));
        }
        return buffer;
    }
}
