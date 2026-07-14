using System;

namespace Liveolator.Core.Tests.Analysis.Cues;

/// <summary>
/// Synthetic audio shaped like a real EDM track, used by the auto-cue tests. A continuous 120-BPM click
/// train gives a strong, detectable tempo, with band content layered on so the band-energy detector sees
/// real structure: 60 Hz "kick" energy during the drops, a 1 kHz tone over intro/breakdown, and a 6 kHz
/// riser for the build-up.
/// </summary>
internal static class CueTestSignals
{
    public const int SampleRate = 44_100;

    public static float[] StructuredClickTrack()
    {
        var buffer = TestSignals.ClickTrain(120, SampleRate, seconds: 48, offsetSeconds: 2.0);
        Silence(buffer, 46, 48);                  // trailing silence -> outro edge

        AddTone(buffer, 1000, 0.5, 2, 10);        // intro (no kick)
        AddTone(buffer, 60, 0.9, 10, 26);         // drop 1 (kick)
        AddTone(buffer, 1000, 0.5, 26, 34);       // breakdown (melodic, no kick)
        AddRamp(buffer, 6000, 0.2, 0.9, 34, 38);  // build-up riser
        AddTone(buffer, 60, 0.9, 38, 46);         // drop 2 (kick)
        return buffer;
    }

    private static void AddTone(float[] buffer, double freq, double amp, double startSec, double endSec)
    {
        int start = (int)(startSec * SampleRate), end = Math.Min((int)(endSec * SampleRate), buffer.Length);
        double w = 2.0 * Math.PI * freq / SampleRate;
        for (int i = start; i < end; i++)
            buffer[i] += (float)(amp * Math.Sin(w * (i - start)));
    }

    private static void AddRamp(
        float[] buffer, double freq, double ampFrom, double ampTo, double startSec, double endSec)
    {
        int start = (int)(startSec * SampleRate), end = Math.Min((int)(endSec * SampleRate), buffer.Length);
        double w = 2.0 * Math.PI * freq / SampleRate;
        int span = Math.Max(1, end - start);
        for (int i = start; i < end; i++)
        {
            double amp = ampFrom + (ampTo - ampFrom) * ((double)(i - start) / span);
            buffer[i] += (float)(amp * Math.Sin(w * (i - start)));
        }
    }

    private static void Silence(float[] buffer, double startSec, double endSec)
    {
        int start = (int)(startSec * SampleRate), end = Math.Min((int)(endSec * SampleRate), buffer.Length);
        for (int i = start; i < end; i++)
            buffer[i] = 0f;
    }
}
