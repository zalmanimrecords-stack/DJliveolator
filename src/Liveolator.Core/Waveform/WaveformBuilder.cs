namespace Liveolator.Core.Waveform;

/// <summary>
/// Reduces mono PCM to a fixed-size <see cref="WaveformOverview"/>: each bucket holds the maximum
/// absolute amplitude over its slice of the track, so transients survive the downsample (averaging
/// would wash them out). Pure and allocation-light — the decode that feeds it lives in the audio binding.
/// </summary>
public static class WaveformBuilder
{
    /// <summary>
    /// Build an overview of <paramref name="bucketCount"/> peaks from <paramref name="monoSamples"/>.
    /// Empty input yields <see cref="WaveformOverview.Empty"/>; when there are fewer samples than
    /// buckets every bucket is still filled (the data is upsampled, never dropped). Amplitudes are
    /// clamped to 0..1 so a hot sample can't overflow the strip.
    /// </summary>
    public static WaveformOverview Build(ReadOnlySpan<float> monoSamples, int bucketCount)
    {
        if (bucketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), bucketCount, "Bucket count must be positive.");
        if (monoSamples.IsEmpty)
            return WaveformOverview.Empty;

        int total = monoSamples.Length;
        var peaks = new float[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            // Proportional slice [start, end); guarantee at least one sample so no bucket is left blank
            // when there are more buckets than samples.
            int start = (int)((long)i * total / bucketCount);
            int end = (int)((long)(i + 1) * total / bucketCount);
            if (end <= start)
                end = start + 1;

            float max = 0f;
            for (int s = start; s < end; s++)
            {
                float amplitude = Math.Abs(monoSamples[s]);
                if (amplitude > max)
                    max = amplitude;
            }
            peaks[i] = max > 1f ? 1f : max;
        }

        return new WaveformOverview(peaks);
    }
}
