using System;
using System.Collections.Generic;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;

namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// Detects a track's musical structure from its band-energy contour with a <em>novelty curve</em>:
/// where the spectral balance changes fastest is where the arrangement changes. Pure C# — no Python,
/// no model, no native — so every track gets a <see cref="SongStructure"/> without the optional
/// librosa runtime (<see cref="ISongStructureAnalyzer"/>, doc 32), which stays the higher-quality
/// override when it is installed.
/// </summary>
/// <remarks>
/// Unlike <see cref="StructuralCueDetector"/>, which reads structure through EDM-specific rules (kick
/// present ⇒ drop, kick absent ⇒ breakdown) and finds one of each, this finds <em>every</em> real
/// change point regardless of genre and then labels it. The two are complementary: this one is the
/// primary source, and the rule-based detector remains the fallback for material too flat to read —
/// which is why <see cref="Detect"/> returns <c>null</c> rather than guessing.
/// <para>
/// Method (ported from the WebClip analyzer): the per-frame bands are averaged down to ~10 Hz and
/// normalized to their own peaks, novelty is the weighted absolute delta between adjacent samples,
/// peaks above an adaptive threshold (mean + σ·1.15, with an absolute floor so flat material cannot
/// cross it) become boundaries, and each boundary is labelled from the energy/bass change across it.
/// </para>
/// <para>
/// One thing this adds over WebClip, which has no tempo: boundaries are snapped to the bar grid.
/// A novelty peak lands a few hundred ms off the downbeat that caused it (STFT hop + smoothing), and
/// consumers reject a structure whose boundaries drift off the grid they are mixing on — librosa's
/// output is beat-synchronous by construction, so this is what makes the two interchangeable.
/// </para>
/// </remarks>
public sealed class NoveltyStructureDetector
{
    /// <summary>Provenance written to <see cref="SongStructure.AnalyzedWith"/>.</summary>
    public const string Provenance = "novelty v1";

    // Band weights: broadband loudness dominates, then the kick/bass band. Mid and high mostly
    // separate a riser or a filter sweep from a full arrangement change.
    private const double BroadbandWeight = 0.42;
    private const double LowWeight = 0.28;
    private const double MidWeight = 0.18;
    private const double HighWeight = 0.12;

    // Novelty is a delta between adjacent samples of peak-normalized bands, so it sits near zero;
    // this gain lifts it into the same range as ThresholdFloor.
    // ponytail: gain/floor/deltas are calibrated constants, not derived — re-tune against a real
    // track corpus if boundaries come out too dense or too sparse.
    private const double NoveltyGain = 4.5;
    private const double ThresholdFloor = 0.12;
    private const double ThresholdSigmas = 1.15;
    private const int SmoothingRadius = 1;

    // Energy/bass change across a boundary that separates the section labels, measured over
    // ContextSeconds either side, on the same 0..1 normalized scale as the bands.
    private const double ContextSeconds = 2.0;
    private const double DropEnergyDelta = 0.09;
    private const double DropBassDelta = 0.055;
    private const double RiseEnergyDelta = 0.07;

    /// <summary>Below this many detected boundaries there is no structure worth anchoring a mix on,
    /// and the caller is better served by the rule-based fallback.</summary>
    private const int MinBoundaries = 2;

    private readonly double _samplesPerSecond;
    private readonly double _minSpacingSeconds;

    /// <param name="samplesPerSecond">Rate the band contour is averaged down to before novelty is
    /// measured; low enough that a single kick is not a "change", high enough to place a boundary
    /// within a beat.</param>
    /// <param name="minSpacingSeconds">Minimum gap between boundaries. A 16-bar phrase is ~30 s at
    /// 128 BPM, so this is deliberately far above WebClip's 6 s clip-length default.</param>
    public NoveltyStructureDetector(double samplesPerSecond = 10.0, double minSpacingSeconds = 12.0)
    {
        if (samplesPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(samplesPerSecond), "Must be positive.");
        if (minSpacingSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(minSpacingSeconds), "Must be positive.");

        _samplesPerSecond = samplesPerSecond;
        _minSpacingSeconds = minSpacingSeconds;
    }

    /// <summary>
    /// Detects the structure of a track from its band-energy frames. Returns <c>null</c> when there is
    /// nothing readable — no frames, or fewer than <see cref="MinBoundaries"/> change points — so the
    /// caller falls back to the rule-based path instead of committing to a one-section guess.
    /// Deterministic: the same frames always yield the same sections (they are cached to the catalog).
    /// </summary>
    /// <param name="bands">Per-frame band energies for the whole track.</param>
    /// <param name="grid">The track's beat grid; boundaries snap to its bar lines. Pass <c>null</c> (or a
    /// grid with no tempo) to get the raw novelty positions instead.</param>
    public SongStructure? Detect(BandEnergyFrames bands, BeatGrid? grid = null)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.FrameCount == 0 || bands.FrameRateHz <= 0.0)
            return null;

        Samples samples = Downsample(bands);
        if (samples.Count < 3)
            return null;

        int[] boundaries = PickPeaks(Novelty(samples), samples.Rate);
        if (boundaries.Length < MinBoundaries)
            return null;

        return new SongStructure(Label(boundaries, samples, grid), Provenance);
    }

    /// <summary>The band contour at the novelty sample rate, each band normalized to its own peak.</summary>
    private readonly record struct Samples(
        double[] Broadband, double[] Low, double[] Mid, double[] High, double Rate)
    {
        public int Count => Broadband.Length;

        public double Seconds(int index) => Rate > 0.0 ? index / Rate : 0.0;
    }

    private Samples Downsample(BandEnergyFrames bands)
    {
        int framesPerSample = Math.Max(1, (int)Math.Round(bands.FrameRateHz / _samplesPerSecond));
        int count = bands.FrameCount / framesPerSample;
        if (count < 1)
            return new Samples([], [], [], [], 0.0);

        var broadband = new double[count];
        var low = new double[count];
        var mid = new double[count];
        var high = new double[count];

        for (int s = 0; s < count; s++)
        {
            int start = s * framesPerSample;
            int end = start + framesPerSample;
            double sumBroad = 0.0, sumLow = 0.0, sumMid = 0.0, sumHigh = 0.0;
            for (int f = start; f < end; f++)
            {
                sumBroad += bands.Broadband[f];
                sumLow += bands.Low[f];
                sumMid += bands.Mid[f];
                sumHigh += bands.High[f];
            }
            broadband[s] = sumBroad / framesPerSample;
            low[s] = sumLow / framesPerSample;
            mid[s] = sumMid / framesPerSample;
            high[s] = sumHigh / framesPerSample;
        }

        // BandEnergyFrames carries raw magnitude sums; the novelty weights and every threshold below
        // assume a 0..1 scale, so each band is normalized against its own peak over the track.
        NormalizeToPeak(broadband);
        NormalizeToPeak(low);
        NormalizeToPeak(mid);
        NormalizeToPeak(high);

        return new Samples(broadband, low, mid, high, bands.FrameRateHz / framesPerSample);
    }

    private static void NormalizeToPeak(double[] values)
    {
        double max = 0.0;
        foreach (double value in values)
            if (value > max) max = value;
        if (max <= 0.0)
            return;
        for (int i = 0; i < values.Length; i++)
            values[i] /= max;
    }

    private static double[] Novelty(Samples samples)
    {
        var raw = new double[samples.Count];
        for (int i = 1; i < samples.Count; i++)
        {
            raw[i] = Clamp01(
                Math.Abs(samples.Broadband[i] - samples.Broadband[i - 1]) * BroadbandWeight
                + Math.Abs(samples.Low[i] - samples.Low[i - 1]) * LowWeight
                + Math.Abs(samples.Mid[i] - samples.Mid[i - 1]) * MidWeight
                + Math.Abs(samples.High[i] - samples.High[i - 1]) * HighWeight);
        }

        // Smooth so a single loud transient is not a boundary, then lift into the threshold's range.
        var smoothed = new double[samples.Count];
        for (int i = 0; i < smoothed.Length; i++)
            smoothed[i] = Clamp01(Average(raw, i - SmoothingRadius, i + SmoothingRadius + 1) * NoveltyGain);
        return smoothed;
    }

    private int[] PickPeaks(double[] novelty, double rate)
    {
        double mean = Average(novelty, 0, novelty.Length);
        double variance = 0.0;
        foreach (double value in novelty)
            variance += (value - mean) * (value - mean);
        variance /= novelty.Length;

        // Adaptive: a busy track needs a higher bar than a sparse one. The floor is what stops flat
        // material — where mean+σ is tiny — from turning its own noise into boundaries.
        double threshold = Math.Max(ThresholdFloor, mean + Math.Sqrt(variance) * ThresholdSigmas);
        int minSpacing = Math.Max(1, (int)Math.Round(_minSpacingSeconds * rate));

        var candidates = new List<(int Index, double Strength)>();
        for (int i = 1; i < novelty.Length - 1; i++)
        {
            if (novelty[i] >= threshold && novelty[i] >= novelty[i - 1] && novelty[i] >= novelty[i + 1])
                candidates.Add((i, novelty[i]));
        }

        // Strongest first, so the clearest change wins when two candidates are closer than minSpacing.
        // Equal strengths tie-break on index — List.Sort is unstable and this result is cached.
        candidates.Sort((a, b) => a.Strength.Equals(b.Strength)
            ? a.Index.CompareTo(b.Index)
            : b.Strength.CompareTo(a.Strength));

        var accepted = new List<int>();
        foreach ((int index, _) in candidates)
        {
            if (accepted.TrueForAll(taken => Math.Abs(taken - index) >= minSpacing))
                accepted.Add(index);
        }

        accepted.Sort();
        return accepted.ToArray();
    }

    private static List<SongSection> Label(int[] boundaries, Samples samples, BeatGrid? grid)
    {
        int context = Math.Max(1, (int)Math.Round(ContextSeconds * samples.Rate));
        var sections = new List<SongSection>(boundaries.Length + 1)
        {
            new(0.0, SongSectionLabel.Intro),
        };
        double previousSeconds = 0.0;

        for (int b = 0; b < boundaries.Length; b++)
        {
            int index = boundaries[b];
            double seconds = samples.Seconds(index);
            if (grid is { HasTempo: true })
                seconds = grid.NearestDownbeatTo(seconds);
            // A snap that lands on or before the previous section would emit two sections at one point.
            // Unreachable while the minimum spacing exceeds a bar; kept so a re-tune cannot break the shape.
            if (seconds <= previousSeconds)
                continue;

            double energyDelta = Average(samples.Broadband, index, index + context)
                - Average(samples.Broadband, index - context, index);
            double bassDelta = Average(samples.Low, index, index + context)
                - Average(samples.Low, index - context, index);

            // The last change point is the outro only when energy actually falls there; a track whose
            // final detected change is a second drop must not get a mix-out cue on top of it.
            bool isLast = b == boundaries.Length - 1;
            string label =
                isLast && energyDelta < 0.0 ? SongSectionLabel.Outro
                : energyDelta > DropEnergyDelta && bassDelta > DropBassDelta ? SongSectionLabel.Drop
                : energyDelta > RiseEnergyDelta ? SongSectionLabel.BuildUp
                : energyDelta < -RiseEnergyDelta ? SongSectionLabel.Breakdown
                : SongSectionLabel.Section;

            sections.Add(new SongSection(seconds, label));
            previousSeconds = seconds;
        }

        return sections;
    }

    /// <summary>Mean of <paramref name="values"/> over [start, end), clamped to the array.</summary>
    private static double Average(double[] values, int start, int end)
    {
        double sum = 0.0;
        int count = 0;
        for (int i = Math.Max(0, start); i < Math.Min(values.Length, end); i++)
        {
            sum += values[i];
            count++;
        }
        return count > 0 ? sum / count : 0.0;
    }

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
}
