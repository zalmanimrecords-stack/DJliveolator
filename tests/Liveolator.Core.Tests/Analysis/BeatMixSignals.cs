namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// Synthetic-but-realistic beat-detection fixtures with known ground truth. Where <see cref="TestSignals"/>
/// emits a single onset per beat (which both a broadband and a kick-band detector lock to identically),
/// these mixes deliberately place <em>competing</em> energy off the kick — a sustained bassline in the
/// kick band on the off-beat, and bright hats in the broadband on the off-beat — so a phase anchor that
/// follows the loudest broadband transient locks to the off-beat, while one that follows the kick locks
/// to the down-beat. This is the regression fixture for the "deck sync aligns to hats, not the kick" bug
/// and the measurement harness for any later percussive-separation (HPSS) work.
/// </summary>
internal static class BeatMixSignals
{
    /// <summary>
    /// A four-on-the-floor mix: a decaying low kick on every beat (starting <paramref name="kickOffsetSeconds"/>
    /// into the track), plus a sustained bass tone and a bright hat on every off-beat. The kick marks the
    /// true beat phase; the off-beat bass+hat are the pollutants. Peak-normalised so analysis sees no clipping.
    /// </summary>
    public static float[] KickBassHatsFourOnFloor(
        double bpm,
        int sampleRate,
        double seconds,
        double kickOffsetSeconds = 0.0,
        double kickHz = 55.0,
        double bassHz = 110.0,
        double hatHz = 9_000.0,
        double bassAmplitude = 0.9,
        double hatAmplitude = 0.8)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        double firstKick = kickOffsetSeconds * sampleRate;
        double offbeatShift = samplesPerBeat / 2.0;

        for (double beatPos = firstKick; beatPos < total; beatPos += samplesPerBeat)
        {
            AddKick(buffer, (int)beatPos, sampleRate, kickHz);
            AddBass(buffer, (int)(beatPos + offbeatShift), sampleRate, samplesPerBeat, bassHz, bassAmplitude);
            AddHat(buffer, (int)(beatPos + offbeatShift), sampleRate, hatHz, hatAmplitude);
        }

        Normalize(buffer);
        return buffer;
    }

    // A real-kick shape: a short broadband attack CLICK plus a low-frequency BODY. The click deposits
    // strike energy across the low spectrum (a vertical stroke), which is what lets percussive/HPSS
    // separation tell a kick apart from a narrow-band sustained bass note; the body carries the thump.
    private static void AddKick(float[] buffer, int start, int sampleRate, double freqHz)
    {
        int clickLen = (int)(0.004 * sampleRate);
        for (int i = 0; i < clickLen && start + i < buffer.Length && start + i >= 0; i++)
        {
            double decay = (double)(clickLen - i) / clickLen; // sharp transient => broadband
            buffer[start + i] += (float)(0.8 * decay);
        }

        int len = (int)(0.06 * sampleRate);
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        double bodyDecay = 30.0 / sampleRate; // ~fast exponential thump
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
            buffer[start + i] += (float)(Math.Exp(-bodyDecay * i) * Math.Sin(w * i));
    }

    // A sustained bass note filling most of the off-beat — sits in the kick band (<=200 Hz) but off the beat.
    private static void AddBass(
        float[] buffer, int start, int sampleRate, double samplesPerBeat, double freqHz, double amplitude)
    {
        int len = (int)(samplesPerBeat * 0.45);
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        int fade = (int)(0.005 * sampleRate); // short fades so the note's own edges aren't kick-like transients
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
        {
            double env = 1.0;
            if (i < fade) env = (double)i / fade;
            else if (i > len - fade) env = (double)(len - i) / fade;
            buffer[start + i] += (float)(amplitude * env * Math.Sin(w * i));
        }
    }

    // A bright, very short tick — strong broadband onset flux on the off-beat.
    private static void AddHat(float[] buffer, int start, int sampleRate, double freqHz, double amplitude)
    {
        int len = (int)(0.012 * sampleRate);
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        double decay = 400.0 / sampleRate;
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
            buffer[start + i] += (float)(amplitude * Math.Exp(-decay * i) * Math.Sin(w * i));
    }

    private static void Normalize(float[] buffer)
    {
        float peak = 0f;
        foreach (float s in buffer)
        {
            float a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        if (peak <= 0f) return;
        float gain = 0.98f / peak;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= gain;
    }

    /// <summary>Smallest absolute distance between two phases on a circle of circumference <paramref name="period"/>.</summary>
    public static double CircularDistanceSeconds(double a, double b, double period)
    {
        double d = Math.Abs(a - b) % period;
        return Math.Min(d, period - d);
    }
}
