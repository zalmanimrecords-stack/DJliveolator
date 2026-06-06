namespace Liveolator.Core.Waveform;

/// <summary>
/// Reduces mono PCM to a fixed-size <see cref="WaveformOverview"/>: each bucket holds the maximum
/// absolute amplitude over its slice of the track, so transients survive the downsample (averaging
/// would wash them out). Pure and allocation-light — the decode that feeds it lives in the audio binding.
/// </summary>
public static class WaveformBuilder
{
    /// <summary>
    /// Low-pass corner (Hz) for the kick/bass band. ~150 Hz keeps the kick fundamental + first harmonics
    /// while attenuating mids/highs, so the resulting envelope spikes on each kick — a beat-align guide.
    /// </summary>
    public const double KickBandCutoffHz = 150.0;

    /// <summary>
    /// Build an overview of <paramref name="bucketCount"/> peaks from <paramref name="monoSamples"/>.
    /// Empty input yields <see cref="WaveformOverview.Empty"/>; when there are fewer samples than
    /// buckets every bucket is still filled (the data is upsampled, never dropped). Amplitudes are
    /// clamped to 0..1 so a hot sample can't overflow the strip.
    ///
    /// When <paramref name="sampleRate"/> is positive, a low-frequency (kick) band envelope is computed
    /// alongside the broadband peaks via a one-pole low-pass at <see cref="KickBandCutoffHz"/>, populating
    /// <see cref="WaveformOverview.LowPeaks"/>; pass 0 to skip it (broadband only).
    /// </summary>
    public static WaveformOverview Build(ReadOnlySpan<float> monoSamples, int bucketCount, int sampleRate = 0)
    {
        if (bucketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), bucketCount, "Bucket count must be positive.");
        if (monoSamples.IsEmpty)
            return WaveformOverview.Empty;

        bool withLowBand = sampleRate > 0;
        int total = monoSamples.Length;
        var peaks = new float[bucketCount];
        float[]? lowPeaks = withLowBand ? new float[bucketCount] : null;

        // One-pole low-pass state, carried continuously across buckets (samples are processed in order),
        // so the kick envelope is a true running filter rather than a per-bucket reset.
        float lpCoeff = withLowBand
            ? (float)(1.0 - Math.Exp(-2.0 * Math.PI * KickBandCutoffHz / sampleRate))
            : 0f;
        float lpState = 0f;

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
            for (int s = start; s < end; s++)
            {
                float sample = monoSamples[s];
                float amplitude = Math.Abs(sample);
                if (amplitude > max)
                    max = amplitude;

                if (withLowBand)
                {
                    lpState += lpCoeff * (sample - lpState);
                    float lowAmplitude = Math.Abs(lpState);
                    if (lowAmplitude > lowMax)
                        lowMax = lowAmplitude;
                }
            }

            peaks[i] = max > 1f ? 1f : max;
            if (lowPeaks is not null)
                lowPeaks[i] = lowMax > 1f ? 1f : lowMax;
        }

        return new WaveformOverview(peaks, LowPeaks: lowPeaks);
    }
}
