namespace Liveolator.Core.Waveform;

/// <summary>
/// Reduces mono PCM to a fixed-size <see cref="WaveformOverview"/>: each bucket holds the maximum
/// absolute amplitude over its slice of the track, so transients survive the downsample (averaging
/// would wash them out). Alongside the broadband peaks it splits the signal into three time-aligned
/// frequency bands (low/kick · mid · high) via serial biquad filters, the data behind the layered
/// deck strip where the kick band draws in front. Pure and allocation-light — the decode that feeds
/// it lives in the audio binding.
/// </summary>
public static class WaveformBuilder
{
    /// <summary>
    /// Low/mid crossover (Hz). 200 Hz keeps the kick fundamental + punch in the low band while keeping
    /// bass-line/tom bleed out of it (Mixxx bands at 600 Hz; tighter on purpose — the low band here is
    /// the beat-align anchor, so it must spike on kicks and stay quiet between them).
    /// </summary>
    public const double LowCrossoverHz = 200.0;

    /// <summary>Mid/high crossover (Hz): above this is hats/air, drawn as pale caps.</summary>
    public const double HighCrossoverHz = 2_000.0;

    /// <summary>
    /// Build an overview of <paramref name="bucketCount"/> peaks from <paramref name="monoSamples"/>.
    /// Empty input yields <see cref="WaveformOverview.Empty"/>; when there are fewer samples than
    /// buckets every bucket is still filled (the data is upsampled, never dropped). Amplitudes are
    /// clamped to 0..1 so a hot sample can't overflow the strip.
    ///
    /// When <paramref name="sampleRate"/> is positive, the low/mid/high band envelopes are computed
    /// alongside the broadband peaks (4th-order Linkwitz-Riley splits — two cascaded Butterworth
    /// sections per crossover, −24 dB/oct — at <see cref="LowCrossoverHz"/> /
    /// <see cref="HighCrossoverHz"/>), populating <see cref="WaveformOverview.LowPeaks"/> /
    /// <see cref="WaveformOverview.MidPeaks"/> / <see cref="WaveformOverview.HighPeaks"/>. A band whose
    /// crossover doesn't fit under Nyquist degrades away (e.g. a very low decode rate yields only the
    /// low band). Pass 0 to skip banding entirely (broadband only).
    /// </summary>
    public static WaveformOverview Build(ReadOnlySpan<float> monoSamples, int bucketCount, int sampleRate = 0)
    {
        if (bucketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), bucketCount, "Bucket count must be positive.");
        if (monoSamples.IsEmpty)
            return WaveformOverview.Empty;

        // Each band exists only when its crossover sits under Nyquist; otherwise it degrades away
        // rather than designing an unstable filter.
        bool withLow = sampleRate > 2 * LowCrossoverHz;
        bool withMidHigh = sampleRate > 2 * HighCrossoverHz;

        int total = monoSamples.Length;
        var peaks = new float[bucketCount];
        float[]? lowPeaks = withLow ? new float[bucketCount] : null;
        float[]? midPeaks = withMidHigh ? new float[bucketCount] : null;
        float[]? highPeaks = withMidHigh ? new float[bucketCount] : null;

        // Filter state is carried continuously across buckets (samples are processed in order), so each
        // band is a true running filter rather than a per-bucket reset. Every crossover edge is a
        // 4th-order Linkwitz-Riley (two identical Butterworth sections in series): the −24 dB/oct slope
        // keeps basslines/vocals from ghosting into the kick layer, which a single section's −12 dB/oct
        // measurably does not. The mid band shares the exact crossover frequencies with its neighbours.
        BiquadFilter[]? lowChain = withLow
            ? new[] { BiquadFilter.LowPass(LowCrossoverHz, sampleRate), BiquadFilter.LowPass(LowCrossoverHz, sampleRate) }
            : null;
        BiquadFilter[]? midChain = withMidHigh
            ? new[]
            {
                BiquadFilter.HighPass(LowCrossoverHz, sampleRate), BiquadFilter.HighPass(LowCrossoverHz, sampleRate),
                BiquadFilter.LowPass(HighCrossoverHz, sampleRate), BiquadFilter.LowPass(HighCrossoverHz, sampleRate),
            }
            : null;
        BiquadFilter[]? highChain = withMidHigh
            ? new[] { BiquadFilter.HighPass(HighCrossoverHz, sampleRate), BiquadFilter.HighPass(HighCrossoverHz, sampleRate) }
            : null;

        for (int i = 0; i < bucketCount; i++)
        {
            // Proportional slice [start, end); guarantee at least one sample so no bucket is left blank
            // when there are more buckets than samples.
            int start = (int)((long)i * total / bucketCount);
            int end = (int)((long)(i + 1) * total / bucketCount);
            if (end <= start)
                end = start + 1;

            float max = 0f;
            float lowMax = 0f;
            float midMax = 0f;
            float highMax = 0f;
            for (int s = start; s < end; s++)
            {
                float sample = monoSamples[s];
                float amplitude = Math.Abs(sample);
                if (amplitude > max)
                    max = amplitude;

                if (lowChain is not null)
                {
                    float low = Math.Abs(ProcessChain(lowChain, sample));
                    if (low > lowMax)
                        lowMax = low;
                }

                if (midChain is not null)
                {
                    float mid = Math.Abs(ProcessChain(midChain, sample));
                    if (mid > midMax)
                        midMax = mid;

                    float high = Math.Abs(ProcessChain(highChain!, sample));
                    if (high > highMax)
                        highMax = high;
                }
            }

            peaks[i] = max > 1f ? 1f : max;
            if (lowPeaks is not null)
                lowPeaks[i] = lowMax > 1f ? 1f : lowMax;
            if (midPeaks is not null)
                midPeaks[i] = midMax > 1f ? 1f : midMax;
            if (highPeaks is not null)
                highPeaks[i] = highMax > 1f ? 1f : highMax;
        }

        return new WaveformOverview(peaks, LowPeaks: lowPeaks, MidPeaks: midPeaks, HighPeaks: highPeaks);
    }

    private static float ProcessChain(BiquadFilter[] chain, float sample)
    {
        float y = sample;
        for (int i = 0; i < chain.Length; i++)
            y = chain[i].Process(y);
        return y;
    }
}
