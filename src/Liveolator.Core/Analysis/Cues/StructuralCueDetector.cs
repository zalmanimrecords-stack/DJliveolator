using System;
using System.Collections.Generic;
using Liveolator.Core.Analysis.Bpm;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Finds the musical structure points of a track — track start, drop, breakdown, build-up, outro and
/// phrase boundaries — from its band-energy contour and tempo (doc 11/16 phrase analysis). Pure and
/// hardware-free.
/// </summary>
/// <remarks>
/// The method is deliberately heuristic, not ML: the low (kick) band drives drop/breakdown detection,
/// the high band drives build-up detection, and every candidate is quantized to a phrase boundary
/// anchored at the first audible downbeat — which is what makes auto-cues feel "right" to a DJ.
/// <para>
/// It is intentionally <em>conservative</em> (owner decision 2026-06-19): when the tempo confidence is
/// low or the energy contour is too flat to read structure, it emits only the always-safe
/// <see cref="StructuralCueKind.TrackStart"/> + <see cref="StructuralCueKind.OutroStart"/> pair rather
/// than guessing — a wrong drop cue is worse than an empty pad.
/// </para>
/// </remarks>
public sealed class StructuralCueDetector
{
    private readonly int _phraseBars;
    private readonly double _bpmConfidenceFloor;
    private readonly double _kickActiveFraction;
    private readonly double _flatContrastThreshold;
    private readonly int _minSustainBars;
    private readonly int _minBreakdownBars;
    private readonly double _breakdownBroadbandFloor;

    /// <param name="phraseBars">Bars per phrase used for quantization (8/16/32); default 16.</param>
    /// <param name="bpmConfidenceFloor">Below this tempo confidence, only the safe cue pair is placed.</param>
    /// <param name="kickActiveFraction">A bar counts as "kick present" when its low-band energy is at
    /// least this fraction of the track's peak bar low-band energy.</param>
    /// <param name="flatContrastThreshold">Minimum low-band dynamic range (0..1) for structure to be
    /// considered readable; flatter than this is treated as low confidence.</param>
    /// <param name="minSustainBars">Bars the kick must stay present to count as a drop.</param>
    /// <param name="minBreakdownBars">Bars the kick must stay absent to count as a breakdown.</param>
    /// <param name="breakdownBroadbandFloor">A breakdown must keep broadband energy above this fraction
    /// of peak (so a silent gap is not mistaken for a melodic breakdown).</param>
    public StructuralCueDetector(
        int phraseBars = 16,
        double bpmConfidenceFloor = 0.5,
        double kickActiveFraction = 0.4,
        double flatContrastThreshold = 0.3,
        int minSustainBars = 4,
        int minBreakdownBars = 4,
        double breakdownBroadbandFloor = 0.15)
    {
        if (phraseBars < 1)
            throw new ArgumentOutOfRangeException(nameof(phraseBars), "Phrase length must be at least 1 bar.");
        if (bpmConfidenceFloor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(bpmConfidenceFloor), "Must be in [0, 1].");
        if (kickActiveFraction is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(kickActiveFraction), "Must be in (0, 1).");
        if (flatContrastThreshold is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(flatContrastThreshold), "Must be in [0, 1).");
        if (minSustainBars < 1)
            throw new ArgumentOutOfRangeException(nameof(minSustainBars), "Must be at least 1.");
        if (minBreakdownBars < 1)
            throw new ArgumentOutOfRangeException(nameof(minBreakdownBars), "Must be at least 1.");
        if (breakdownBroadbandFloor is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(breakdownBroadbandFloor), "Must be in [0, 1).");

        _phraseBars = phraseBars;
        _bpmConfidenceFloor = bpmConfidenceFloor;
        _kickActiveFraction = kickActiveFraction;
        _flatContrastThreshold = flatContrastThreshold;
        _minSustainBars = minSustainBars;
        _minBreakdownBars = minBreakdownBars;
        _breakdownBroadbandFloor = breakdownBroadbandFloor;
    }

    /// <summary>
    /// Detects the structural cues of a track. Returns <see cref="StructuralCueResult.Empty"/> when there
    /// is no usable beat grid (no frames, or tempo undetectable).
    /// </summary>
    /// <param name="bands">Per-frame band energies (from <see cref="BandEnergyEnvelope"/>).</param>
    /// <param name="bpm">Tempo result — supplies BPM, confidence and the first-beat anchor.</param>
    /// <param name="silenceCues">Silence-detected intro/outro edges (from <see cref="SilenceCueDetector"/>).</param>
    /// <param name="durationSeconds">Track duration in seconds (fallback for the outro edge).</param>
    public StructuralCueResult Detect(
        BandEnergyFrames bands, BpmResult bpm, TrackCues silenceCues, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(bands);
        ArgumentNullException.ThrowIfNull(bpm);

        if (bands.FrameCount == 0 || bpm.Bpm <= 0.0 || bands.FrameRateHz <= 0.0)
            return StructuralCueResult.Empty;

        double frameRate = bands.FrameRateHz;
        double framesPerBar = 4.0 * 60.0 * frameRate / bpm.Bpm;
        if (framesPerBar < 1.0)
            return StructuralCueResult.Empty;
        double phraseFrames = framesPerBar * _phraseBars;
        int totalFrames = bands.FrameCount;

        double introStartSeconds = Math.Max(0.0, silenceCues.IntroStart?.TotalSeconds ?? 0.0);
        double outroEndSeconds = silenceCues.OutroEnd?.TotalSeconds ?? durationSeconds;
        if (outroEndSeconds <= introStartSeconds)
            outroEndSeconds = durationSeconds > introStartSeconds ? durationSeconds : totalFrames / frameRate;

        int introStartFrame = Clamp((int)Math.Round(introStartSeconds * frameRate), 0, totalFrames - 1);
        int outroEndFrame = Clamp((int)Math.Round(outroEndSeconds * frameRate), introStartFrame + 1, totalFrames);

        BarEnergies bars = AggregateBars(bands, introStartFrame, outroEndFrame, framesPerBar);
        double phraseSeconds = phraseFrames / frameRate;

        var cues = new List<StructuralCue>();

        // Always-safe pair (owner decision: these are placed at every confidence level).
        double startConfidence = silenceCues.IntroStart is not null ? 0.95 : 0.8;
        cues.Add(new StructuralCue(StructuralCueKind.TrackStart, introStartSeconds, startConfidence));

        bool structureReadable = IsStructureReadable(bars, bpm.Confidence);
        double overallConfidence = structureReadable
            ? Clamp01(bpm.Confidence)
            : Clamp01(bpm.Confidence * 0.4);

        int outroBar = -1;
        if (structureReadable)
            AddStructuralCues(cues, bars, bpm.Confidence, introStartFrame, framesPerBar, phraseFrames,
                frameRate, totalFrames, out outroBar);

        cues.Add(new StructuralCue(
            StructuralCueKind.OutroStart,
            OutroStartSeconds(outroBar, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames,
                outroEndSeconds, phraseSeconds, introStartSeconds),
            structureReadable && outroBar >= 0 ? Clamp01(bpm.Confidence) : 0.7));

        cues.Sort((a, b) => a.PositionSeconds.CompareTo(b.PositionSeconds));
        return new StructuralCueResult(cues, overallConfidence);
    }

    /// <summary>Bar-level energy series (one value per whole bar from the intro), kept in lockstep.</summary>
    private readonly record struct BarEnergies(double[] Low, double[] Broadband, double[] High, int[] StartFrame)
    {
        public int Count => Low.Length;
    }

    private static BarEnergies AggregateBars(
        BandEnergyFrames bands, int introStartFrame, int outroEndFrame, double framesPerBar)
    {
        var low = new List<double>();
        var broad = new List<double>();
        var high = new List<double>();
        var starts = new List<int>();

        for (int bar = 0; ; bar++)
        {
            int start = introStartFrame + (int)Math.Round(bar * framesPerBar);
            int end = introStartFrame + (int)Math.Round((bar + 1) * framesPerBar);
            if (end > outroEndFrame || end > bands.FrameCount || start >= end)
                break;

            double sumLow = 0, sumBroad = 0, sumHigh = 0;
            for (int f = start; f < end; f++)
            {
                sumLow += bands.Low[f];
                sumBroad += bands.Broadband[f];
                sumHigh += bands.High[f];
            }
            int n = end - start;
            low.Add(sumLow / n);
            broad.Add(sumBroad / n);
            high.Add(sumHigh / n);
            starts.Add(start);
        }

        return new BarEnergies(low.ToArray(), broad.ToArray(), high.ToArray(), starts.ToArray());
    }

    private bool IsStructureReadable(BarEnergies bars, double bpmConfidence)
    {
        if (bpmConfidence < _bpmConfidenceFloor)
            return false;
        if (bars.Count < _minSustainBars * 2)
            return false;

        double max = 0, min = double.MaxValue;
        foreach (double v in bars.Low)
        {
            if (v > max) max = v;
            if (v < min) min = v;
        }
        if (max <= 0.0)
            return false;

        double contrast = (max - min) / max;
        return contrast >= _flatContrastThreshold;
    }

    private void AddStructuralCues(
        List<StructuralCue> cues, BarEnergies bars, double bpmConfidence, int introStartFrame,
        double framesPerBar, double phraseFrames, double frameRate, int totalFrames, out int outroBar)
    {
        outroBar = -1;

        double maxLow = 0, maxBroad = 0;
        foreach (double v in bars.Low) if (v > maxLow) maxLow = v;
        foreach (double v in bars.Broadband) if (v > maxBroad) maxBroad = v;

        double kickThreshold = maxLow * _kickActiveFraction;
        var kickActive = new bool[bars.Count];
        for (int b = 0; b < bars.Count; b++)
            kickActive[b] = bars.Low[b] >= kickThreshold;

        int dropBar = FindFirstSustainedRun(kickActive, active: true, _minSustainBars);
        if (dropBar >= 0)
        {
            cues.Add(new StructuralCue(
                StructuralCueKind.Drop,
                SnapBarToPhraseSeconds(dropBar, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames),
                Clamp01(bpmConfidence)));
        }

        // Breakdown: first long kick-absent run after the drop that keeps melodic (broadband) energy.
        int searchFrom = dropBar >= 0 ? dropBar + _minSustainBars : 0;
        int breakdownBar = FindBreakdownRun(kickActive, bars.Broadband, maxBroad, searchFrom);
        if (breakdownBar >= 0)
        {
            cues.Add(new StructuralCue(
                StructuralCueKind.Breakdown,
                SnapBarToPhraseSeconds(breakdownBar, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames),
                Clamp01(bpmConfidence)));

            // Build-up: the phrase leading into the kick re-entry that ends the breakdown.
            int reEntryBar = FindNext(kickActive, active: true, breakdownBar + _minBreakdownBars);
            if (reEntryBar >= 0)
            {
                int buildBar = Math.Max(breakdownBar + 1, reEntryBar - _phraseBars);
                double highRise = HighBandRise(bars.High, buildBar, reEntryBar);
                cues.Add(new StructuralCue(
                    StructuralCueKind.BuildUp,
                    SnapBarToPhraseSeconds(buildBar, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames),
                    Clamp01(bpmConfidence * highRise)));
            }
        }

        // Outro: the last kick-present -> kick-absent transition that stays absent to the end.
        outroBar = FindLastTrailingRun(kickActive, active: false, _minSustainBars);

        AddPhraseFillers(cues, bars, dropBar, introStartFrame, framesPerBar, phraseFrames, frameRate,
            totalFrames, bpmConfidence);
    }

    private void AddPhraseFillers(
        List<StructuralCue> cues, BarEnergies bars, int dropBar, int introStartFrame, double framesPerBar,
        double phraseFrames, double frameRate, int totalFrames, double bpmConfidence)
    {
        // Phrase mix points every phrase from the first phrase after the drop onward; the placer dedups
        // these against the named cues and keeps only what bank B can hold.
        int firstBar = (dropBar >= 0 ? dropBar : 0) + _phraseBars;
        for (int b = firstBar; b < bars.Count - _phraseBars; b += _phraseBars)
        {
            cues.Add(new StructuralCue(
                StructuralCueKind.Phrase,
                SnapBarToPhraseSeconds(b, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames),
                Clamp01(bpmConfidence * 0.6)));
        }
    }

    private double OutroStartSeconds(
        int outroBar, BarEnergies bars, int introStartFrame, double framesPerBar, double phraseFrames,
        double frameRate, int totalFrames, double outroEndSeconds, double phraseSeconds, double introStartSeconds)
    {
        if (outroBar >= 0 && outroBar < bars.Count)
            return SnapBarToPhraseSeconds(outroBar, bars, introStartFrame, framesPerBar, phraseFrames, frameRate, totalFrames);

        // Fallback (also the low-confidence path): one phrase before the audible end.
        return Math.Max(introStartSeconds, outroEndSeconds - phraseSeconds);
    }

    private static double HighBandRise(double[] high, int fromBar, int toBar)
    {
        if (toBar <= fromBar || fromBar < 0 || toBar >= high.Length)
            return 0.5;
        double start = Math.Max(high[fromBar], 1e-9);
        double end = high[toBar];
        double ratio = end / start;
        // Map a 1x..3x rise onto 0..1 so a clear riser scores high and a flat region scores low.
        return Clamp01((ratio - 1.0) / 2.0);
    }

    private static int FindFirstSustainedRun(bool[] flags, bool active, int minRun)
    {
        int run = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i] == active)
            {
                if (++run >= minRun)
                    return i - minRun + 1;
            }
            else
            {
                run = 0;
            }
        }
        return -1;
    }

    private int FindBreakdownRun(bool[] kickActive, double[] broadband, double maxBroad, int from)
    {
        double floor = maxBroad * _breakdownBroadbandFloor;
        int run = 0;
        for (int i = Math.Max(0, from); i < kickActive.Length; i++)
        {
            bool quietKick = !kickActive[i] && broadband[i] >= floor;
            if (quietKick)
            {
                if (++run >= _minBreakdownBars)
                    return i - _minBreakdownBars + 1;
            }
            else
            {
                run = 0;
            }
        }
        return -1;
    }

    private static int FindNext(bool[] flags, bool active, int from)
    {
        for (int i = Math.Max(0, from); i < flags.Length; i++)
            if (flags[i] == active)
                return i;
        return -1;
    }

    private static int FindLastTrailingRun(bool[] flags, bool active, int minRun)
    {
        // Walk back from the end: the trailing run must reach the track end and be at least minRun long.
        int end = flags.Length;
        int i = end - 1;
        while (i >= 0 && flags[i] == active)
            i--;
        int runStart = i + 1;
        if (runStart < end && (end - runStart) >= minRun && runStart > 0)
            return runStart;
        return -1;
    }

    private static double SnapBarToPhraseSeconds(
        int bar, BarEnergies bars, int introStartFrame, double framesPerBar, double phraseFrames,
        double frameRate, int totalFrames)
    {
        int frame = bar >= 0 && bar < bars.StartFrame.Length
            ? bars.StartFrame[bar]
            : introStartFrame + (int)Math.Round(bar * framesPerBar);

        double k = Math.Round((frame - introStartFrame) / phraseFrames);
        double snapped = introStartFrame + k * phraseFrames;
        if (snapped < 0) snapped = 0;
        if (snapped > totalFrames) snapped = totalFrames;
        return snapped / frameRate;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
}
