using Liveolator.Core.Waveform;

namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Moves a vouched low-band beat anchor back onto the kick's CLICK. The low band tracks the kick's body,
/// and how long after the click the body arrives is a property of each kick's design — measured on two
/// built sets (2026-10-31) the published grid sat 14-50 ms after the &gt;2 kHz click on every track, so two
/// records aligned grid-to-grid flammed by the DIFFERENCE, 15-24 ms on most joins. The click is the sharp,
/// band-independent instant the ear aligns, so it is the one both records must share.
/// <para>The search is one-sided and short: the click precedes the body, and it stops short of the 16th note
/// before the beat, so neither a 16th hat nor an off-beat hat can be taken for the click. A profile without a
/// clear peak — no click, or a smeared one — leaves the anchor where it was.</para>
/// </summary>
public static class KickClickAnchor
{
    /// <summary>Above the kick body and the bass, below where hats dominate their own energy.</summary>
    public const double HighPassHz = 2000.0;

    /// <summary>How far before the anchor a click may sit. The earliest measured was 87 ms (psytrance).</summary>
    public const double MaxSearchBeforeSeconds = 0.095;

    /// <summary>Share of a 16th note the search may reach back, so a 16th hat stays outside it.</summary>
    public const double SixteenthShare = 0.9;

    /// <summary>Tolerance after the anchor, for a body anchor that already sits on the click.</summary>
    public const double SearchAfterSeconds = 0.010;

    /// <summary>Peak over the profile's median. Measured clicks scored 6-44; a click-less profile stays near 1.</summary>
    public const double MinimumProminence = 4.0;

    private const double BinSeconds = 0.0005;

    /// <summary>
    /// Seconds to add to <paramref name="anchorSeconds"/> so it lands on the click, or 0 when the track
    /// shows no clear click near the anchor.
    /// </summary>
    public static double Offset(ReadOnlySpan<float> mono, int sampleRate, double bpm, double anchorSeconds)
    {
        if (bpm <= 0.0 || sampleRate <= 2 * HighPassHz || mono.Length == 0)
            return 0.0;

        double period = 60.0 / bpm;
        int bins = (int)(period / BinSeconds);
        double[] profile = FoldedHighBand(mono, sampleRate, period, anchorSeconds, bins);

        double searchBefore = Math.Min(MaxSearchBeforeSeconds, SixteenthShare * period / 4.0);
        int before = (int)(searchBefore / BinSeconds);
        int after = (int)(SearchAfterSeconds / BinSeconds);
        int peakOffset = 0;
        double peak = 0.0;
        for (int offset = -before; offset <= after; offset++)
        {
            double value = profile[(offset % bins + bins) % bins];
            if (value > peak)
            {
                peak = value;
                peakOffset = offset;
            }
        }

        double median = Median(profile);
        if (median <= 0.0 || peak / median < MinimumProminence)
            return 0.0;

        return peakOffset * BinSeconds;
    }

    // |high band| folded onto one beat, bin 0 at the anchor's phase.
    private static double[] FoldedHighBand(
        ReadOnlySpan<float> mono, int sampleRate, double period, double anchorSeconds, int bins)
    {
        var sums = new double[bins];
        var counts = new int[bins];
        BiquadFilter first = BiquadFilter.HighPass(HighPassHz, sampleRate);
        BiquadFilter second = BiquadFilter.HighPass(HighPassHz, sampleRate);
        for (int i = 0; i < mono.Length; i++)
        {
            double phase = ((i / (double)sampleRate) - anchorSeconds) / period;
            phase -= Math.Floor(phase);
            int bin = Math.Min(bins - 1, (int)(phase * bins));
            sums[bin] += Math.Abs(second.Process(first.Process(mono[i])));
            counts[bin]++;
        }

        for (int b = 0; b < bins; b++)
            sums[b] = counts[b] > 0 ? sums[b] / counts[b] : 0.0;
        return sums;
    }

    private static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
