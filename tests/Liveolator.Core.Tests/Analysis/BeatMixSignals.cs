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

    /// <summary>
    /// The psytrance shape measured in the field: a deep kick on every beat whose energy is nearly all
    /// <em>low and narrow-band</em>, against a short, bright, BROADBAND off-beat hit.
    /// <para>This separates the two kick-onset envelopes exactly the way real material does. HPSS
    /// (<see cref="Liveolator.Core.Analysis.Bpm.PercussiveOnsetEnvelope"/>) scores a cell by how broadband it
    /// is — the median across frequency — so the off-beat burst masks IN strongly while the narrow 55 Hz kick
    /// body masks itself out, and the percussive envelope's phase lands on the off-beat. The kick band
    /// (<see cref="Liveolator.Core.Analysis.Bpm.LowBandOnsetEnvelope"/>) scores total energy under 200 Hz,
    /// where the long kick body dwarfs the burst's 8 ms of low-frequency content, so its phase lands on the
    /// kick. Measured motivation: on a real 11-track set the shipped (HPSS) anchor sat within 5.8-20.7 ms of
    /// the &gt;6 kHz hat peak on 4 tracks and every join of the built set flammed by 78-205 ms.</para>
    /// </summary>
    public static float[] KickWithLoudOffbeatPercussion(
        double bpm,
        int sampleRate,
        double seconds,
        double kickOffsetSeconds = 0.0,
        double kickHz = 55.0,
        double hatHz = 9_000.0)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        double firstKick = kickOffsetSeconds * sampleRate;
        double offbeatShift = samplesPerBeat / 2.0;
        var noise = new Random(20260813);

        // The rolling sub bassline that every psytrance record has, at the kick's own frequency. It is what
        // makes HPSS lose the kick: the median ALONG TIME at those bins is now the bassline's level, so the
        // kick reads as harmonic and is masked out, while the broadband off-beat burst survives the mask.
        AddSubDrone(buffer, sampleRate, freqHz: 55.0, amplitude: 0.45);

        for (double beatPos = firstKick; beatPos < total; beatPos += samplesPerBeat)
        {
            // A club kick: all body, barely any broadband click — the strike is narrow-band, which is the
            // other half of why a percussive detector discounts it.
            AddKick(buffer, (int)beatPos, sampleRate, kickHz, clickAmplitude: 0.03);
            AddClick(buffer, (int)(beatPos + offbeatShift), sampleRate, amplitude: 0.9, seconds: 0.004);
            AddNoiseBurst(buffer, (int)(beatPos + offbeatShift), sampleRate, noise, amplitude: 0.5);
            AddHat(buffer, (int)(beatPos + offbeatShift), sampleRate, hatHz, amplitude: 0.9);
        }

        Normalize(buffer);
        return buffer;
    }

    // A continuous low tone under the whole track: no onset of its own (constant energy ⇒ no flux), but it
    // dominates the time-median at the kick's bins.
    private static void AddSubDrone(float[] buffer, int sampleRate, double freqHz, double amplitude)
    {
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] += (float)(amplitude * Math.Sin(w * i));
    }

    // A bare pulse: flat from DC to a few hundred Hz, so it is BOTH maximally percussive (broadband ⇒ the
    // median across frequency is high) and present in the kick band — the off-beat hit a percussive detector
    // cannot ignore. Its total low-band ENERGY stays tiny because it lasts a few milliseconds.
    private static void AddClick(float[] buffer, int start, int sampleRate, double amplitude, double seconds)
    {
        int len = Math.Max(1, (int)(seconds * sampleRate));
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
            buffer[start + i] += (float)(amplitude * (double)(len - i) / len);
    }

    // A short broadband burst: flat across the spectrum, so HPSS reads it as strongly percussive, yet it
    // deposits very little energy under 200 Hz because it lasts only 8 ms.
    private static void AddNoiseBurst(
        float[] buffer, int start, int sampleRate, Random noise, double amplitude)
    {
        int len = (int)(0.030 * sampleRate);
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
        {
            double decay = (double)(len - i) / len;
            buffer[start + i] += (float)(amplitude * decay * ((noise.NextDouble() * 2.0) - 1.0));
        }
    }

    /// <summary>
    /// A drum &amp; bass half-time mix at a fast tempo (~170+): kick on beat 1 and snare on beat 3 of every
    /// 4-beat bar, with dense hats on every half-beat. The kick–snare backbone gives strong periodicity at
    /// HALF the true tempo (and slower sub-harmonics like 2.5 beats), the classic trap that makes 174 BPM
    /// read as ~70/87 — the regression fixture for fast-tempo octave errors. The hats carry the true beat.
    /// </summary>
    public static float[] KickSnareHatsDnB(
        double bpm,
        int sampleRate,
        double seconds,
        double kickOffsetSeconds = 0.0,
        double kickHz = 55.0,
        double hatHz = 9_000.0)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        double firstKick = kickOffsetSeconds * sampleRate;

        for (double pos = firstKick; pos < total; pos += samplesPerBeat / 2.0)
            AddHat(buffer, (int)pos, sampleRate, hatHz, amplitude: 0.5);

        for (double barPos = firstKick; barPos < total; barPos += samplesPerBeat * 4.0)
        {
            AddKick(buffer, (int)barPos, sampleRate, kickHz);
            AddSnare(buffer, (int)(barPos + samplesPerBeat * 2.0), sampleRate);
        }

        Normalize(buffer);
        return buffer;
    }

    // A loud mid-band crack: inharmonic partials with a fast decay — a strong broadband transient that
    // stays OUT of the kick band (<200 Hz), so it pollutes the broadband tempo but not the kick fit.
    private static void AddSnare(float[] buffer, int start, int sampleRate)
    {
        int len = (int)(0.08 * sampleRate);
        double w1 = 2.0 * Math.PI * 900.0 / sampleRate;
        double w2 = 2.0 * Math.PI * 1_730.0 / sampleRate;
        double decay = 60.0 / sampleRate;
        for (int i = 0; i < len && start + i < buffer.Length && start + i >= 0; i++)
            buffer[start + i] += (float)(1.2 * Math.Exp(-decay * i) * (Math.Sin(w1 * i) + 0.7 * Math.Sin(w2 * i)));
    }

    // A real-kick shape: a short broadband attack CLICK plus a low-frequency BODY. The click deposits
    // strike energy across the low spectrum (a vertical stroke), which is what lets percussive/HPSS
    // separation tell a kick apart from a narrow-band sustained bass note; the body carries the thump.
    private static void AddKick(
        float[] buffer, int start, int sampleRate, double freqHz, double clickAmplitude = 0.8)
    {
        int clickLen = (int)(0.004 * sampleRate);
        for (int i = 0; i < clickLen && start + i < buffer.Length && start + i >= 0; i++)
        {
            double decay = (double)(clickLen - i) / clickLen; // sharp transient => broadband
            buffer[start + i] += (float)(clickAmplitude * decay);
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
