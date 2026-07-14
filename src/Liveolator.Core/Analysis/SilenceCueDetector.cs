namespace Liveolator.Core.Analysis;

/// <summary>
/// Detects the audible region of a track via windowed RMS: the first/last window above a
/// fraction of the peak level become Intro Start / Outro End (doc 11/16 silence-detected cues).
/// </summary>
public sealed class SilenceCueDetector
{
    private readonly int _windowSamples;
    private readonly double _thresholdFraction;

    public SilenceCueDetector(int windowSamples = 2048, double thresholdFraction = 0.15)
    {
        if (windowSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(windowSamples));
        if (thresholdFraction is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(thresholdFraction), "Must be in (0, 1).");
        _windowSamples = windowSamples;
        _thresholdFraction = thresholdFraction;
    }

    public TrackCues Detect(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0 || mono.Length == 0)
            return TrackCues.None;

        int n = mono.Length;
        int windowCount = (n + _windowSamples - 1) / _windowSamples;
        var rms = new double[windowCount];
        double peak = 0;

        for (int w = 0; w < windowCount; w++)
        {
            int start = w * _windowSamples;
            int end = Math.Min(start + _windowSamples, n);
            double sum = 0;
            for (int i = start; i < end; i++)
                sum += (double)mono[i] * mono[i];
            double value = Math.Sqrt(sum / (end - start));
            rms[w] = value;
            if (value > peak) peak = value;
        }

        if (peak <= 0)
            return TrackCues.None;

        double threshold = peak * _thresholdFraction;
        int first = -1, last = -1;
        for (int w = 0; w < windowCount; w++)
        {
            if (rms[w] >= threshold)
            {
                if (first < 0) first = w;
                last = w;
            }
        }

        if (first < 0)
            return TrackCues.None;

        var introStart = TimeSpan.FromSeconds((double)(first * _windowSamples) / sampleRate);
        int outroEndSample = Math.Min((last + 1) * _windowSamples, n);
        var outroEnd = TimeSpan.FromSeconds((double)outroEndSample / sampleRate);
        return new TrackCues(introStart, IntroEnd: null, OutroStart: null, outroEnd);
    }
}
