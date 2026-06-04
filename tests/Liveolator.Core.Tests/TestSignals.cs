namespace Liveolator.Core.Tests;

/// <summary>Synthetic PCM generators used to assert analysis results against known ground truth.</summary>
internal static class TestSignals
{
    /// <summary>An impulse/click train at a known tempo: short bursts every beat.</summary>
    public static float[] ClickTrain(double bpm, int sampleRate, double seconds, int clickWidth = 8)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        for (double pos = 0; pos < total; pos += samplesPerBeat)
        {
            int start = (int)pos;
            for (int i = 0; i < clickWidth && start + i < total; i++)
                buffer[start + i] = 1.0f;
        }
        return buffer;
    }

    /// <summary>A pure sine tone.</summary>
    public static float[] Sine(double frequencyHz, int sampleRate, double seconds, double amplitude = 1.0)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double w = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (int i = 0; i < total; i++)
            buffer[i] = (float)(amplitude * Math.Sin(w * i));
        return buffer;
    }

    /// <summary>Sum of sine tones (e.g. a chord), each with its own amplitude.</summary>
    public static float[] Chord((double freq, double amp)[] tones, int sampleRate, double seconds)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        foreach (var (freq, amp) in tones)
        {
            double w = 2.0 * Math.PI * freq / sampleRate;
            for (int i = 0; i < total; i++)
                buffer[i] += (float)(amp * Math.Sin(w * i));
        }
        return buffer;
    }
}
