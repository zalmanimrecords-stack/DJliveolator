namespace Liveolator.Core.Mixer;

/// <summary>
/// Pure mixer DSP math (doc 11, "mixer math is pure and unit-tested"): maps the crossfader and
/// channel controls to audible per-deck gains, and designs biquad coefficients for the 3-band EQ
/// and the single-knob filter. No state, no native code, no sample loop — just the numbers the
/// realtime binding needs. Coefficient formulas follow the RBJ Audio-EQ Cookbook.
/// </summary>
public static class MixerMath
{
    // EQ band centre frequencies (Hz) for a classic DJ 3-band split.
    private const double LowBandHz = 100.0;
    private const double MidBandHz = 1_000.0;
    private const double HighBandHz = 8_000.0;

    // Filter knob sweep range (Hz) for the single-knob low-/high-pass.
    private const double FilterMinHz = 30.0;
    private const double FilterMaxHz = 18_000.0;

    // Max EQ boost/cut at a band's extreme (0 or 1) in decibels.
    private const double MaxEqGainDb = 24.0;

    private const double DefaultQ = 0.707; // Butterworth-ish, no resonant peak.

    /// <summary>
    /// Per-deck crossfader gains (gainA, gainB) for a 0..1 fader position and curve. Position is
    /// clamped; 0 = full A, 1 = full B.
    /// </summary>
    public static (double GainA, double GainB) CrossfaderGains(double position, CrossfaderCurve curve)
    {
        double p = Math.Clamp(position, 0.0, 1.0);

        return curve switch
        {
            CrossfaderCurve.Linear => (1.0 - p, p),
            CrossfaderCurve.Smooth => (Math.Cos(p * Math.PI / 2.0), Math.Sin(p * Math.PI / 2.0)),
            CrossfaderCurve.Sharp => SharpGains(p),
            _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown crossfader curve."),
        };
    }

    /// <summary>
    /// The combined linear output gain for one deck slot: its channel gain times the crossfader
    /// gain for that side. This is the scalar the binding multiplies the deck's samples by before
    /// summing into the master bus.
    /// </summary>
    public static double DeckOutputGain(MixerState state, int slot)
    {
        ArgumentNullException.ThrowIfNull(state);
        (double gainA, double gainB) = CrossfaderGains(state.Crossfader, state.Curve);
        double crossfade = slot switch
        {
            MixerState.DeckA => gainA,
            MixerState.DeckB => gainB,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range."),
        };
        return Math.Clamp(state.Channel(slot).Gain, 0.0, 1.0) * crossfade;
    }

    /// <summary>
    /// Designs the biquad for one EQ band from its normalized control (0.5 = flat). Low and High
    /// are shelving filters, Mid is a peaking filter — the standard DJ EQ topology.
    /// </summary>
    public static BiquadCoefficients EqBandCoefficients(EqBand band, EqBands eq, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(eq);
        EnsureSampleRate(sampleRate);

        double control = band switch
        {
            EqBand.Low => eq.Low,
            EqBand.Mid => eq.Mid,
            EqBand.High => eq.High,
            _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown EQ band."),
        };

        double gainDb = (Math.Clamp(control, 0.0, 1.0) - EqBands.Unity) * 2.0 * MaxEqGainDb;
        if (Math.Abs(gainDb) < 1e-6)
            return BiquadCoefficients.Bypass;

        return band switch
        {
            EqBand.Low => LowShelf(LowBandHz, gainDb, sampleRate),
            EqBand.High => HighShelf(HighBandHz, gainDb, sampleRate),
            _ => Peaking(MidBandHz, gainDb, sampleRate),
        };
    }

    /// <summary>
    /// Designs the single-knob filter biquad. <see cref="DeckChannelState.FilterCenter"/> (0.5) is
    /// bypass; below center sweeps a low-pass down from the top, above center sweeps a high-pass up
    /// from the bottom.
    /// </summary>
    public static BiquadCoefficients FilterCoefficients(double knob, int sampleRate)
    {
        EnsureSampleRate(sampleRate);
        double k = Math.Clamp(knob, 0.0, 1.0);

        if (Math.Abs(k - DeckChannelState.FilterCenter) < 1e-6)
            return BiquadCoefficients.Bypass;

        if (k < DeckChannelState.FilterCenter)
        {
            // 0 -> tightest low-pass (FilterMinHz), 0.5 -> wide open (FilterMaxHz).
            double t = k / DeckChannelState.FilterCenter; // 0..1
            double cutoff = LogInterp(FilterMinHz, FilterMaxHz, t);
            return LowPass(cutoff, sampleRate);
        }
        else
        {
            // 0.5 -> fully open high-pass (FilterMinHz), 1 -> tightest (FilterMaxHz).
            double t = (k - DeckChannelState.FilterCenter) / DeckChannelState.FilterCenter; // 0..1
            double cutoff = LogInterp(FilterMinHz, FilterMaxHz, t);
            return HighPass(cutoff, sampleRate);
        }
    }

    // Sharp curve: each deck stays near full while the fader is on its own half, then cuts fast.
    private static (double, double) SharpGains(double p)
        => (Plateau(1.0 - p), Plateau(p));

    // Stays at 1 for x in [0.5,1] (own half), steep falloff to 0 across the far half.
    private static double Plateau(double x)
    {
        if (x >= 0.5) return 1.0;
        double s = x / 0.5;     // 0..1
        return s * s * s * s;   // quartic: near-silent until close to the midpoint
    }

    private static double LogInterp(double min, double max, double t)
        => min * Math.Pow(max / min, Math.Clamp(t, 0.0, 1.0));

    private static void EnsureSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
    }

    // --- RBJ Audio-EQ Cookbook biquad designs (normalized by a0) ---

    private static BiquadCoefficients LowPass(double freq, int sampleRate)
    {
        (double w0, double cosw0, double alpha) = Omega(freq, sampleRate, DefaultQ);
        double b1 = 1.0 - cosw0;
        double b0 = b1 / 2.0;
        double b2 = b0;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cosw0;
        double a2 = 1.0 - alpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static BiquadCoefficients HighPass(double freq, int sampleRate)
    {
        (double w0, double cosw0, double alpha) = Omega(freq, sampleRate, DefaultQ);
        double b0 = (1.0 + cosw0) / 2.0;
        double b1 = -(1.0 + cosw0);
        double b2 = b0;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cosw0;
        double a2 = 1.0 - alpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static BiquadCoefficients Peaking(double freq, double gainDb, int sampleRate)
    {
        (double w0, double cosw0, double alpha) = Omega(freq, sampleRate, DefaultQ);
        double a = Math.Pow(10.0, gainDb / 40.0);
        double b0 = 1.0 + (alpha * a);
        double b1 = -2.0 * cosw0;
        double b2 = 1.0 - (alpha * a);
        double a0 = 1.0 + (alpha / a);
        double a1 = -2.0 * cosw0;
        double a2 = 1.0 - (alpha / a);
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static BiquadCoefficients LowShelf(double freq, double gainDb, int sampleRate)
    {
        (double w0, double cosw0, double alpha) = Omega(freq, sampleRate, DefaultQ);
        double a = Math.Pow(10.0, gainDb / 40.0);
        double sqrtA = Math.Sqrt(a);
        double twoSqrtAAlpha = 2.0 * sqrtA * alpha;

        double b0 = a * ((a + 1.0) - ((a - 1.0) * cosw0) + twoSqrtAAlpha);
        double b1 = 2.0 * a * ((a - 1.0) - ((a + 1.0) * cosw0));
        double b2 = a * ((a + 1.0) - ((a - 1.0) * cosw0) - twoSqrtAAlpha);
        double a0 = (a + 1.0) + ((a - 1.0) * cosw0) + twoSqrtAAlpha;
        double a1 = -2.0 * ((a - 1.0) + ((a + 1.0) * cosw0));
        double a2 = (a + 1.0) + ((a - 1.0) * cosw0) - twoSqrtAAlpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static BiquadCoefficients HighShelf(double freq, double gainDb, int sampleRate)
    {
        (double w0, double cosw0, double alpha) = Omega(freq, sampleRate, DefaultQ);
        double a = Math.Pow(10.0, gainDb / 40.0);
        double sqrtA = Math.Sqrt(a);
        double twoSqrtAAlpha = 2.0 * sqrtA * alpha;

        double b0 = a * ((a + 1.0) + ((a - 1.0) * cosw0) + twoSqrtAAlpha);
        double b1 = -2.0 * a * ((a - 1.0) + ((a + 1.0) * cosw0));
        double b2 = a * ((a + 1.0) + ((a - 1.0) * cosw0) - twoSqrtAAlpha);
        double a0 = (a + 1.0) - ((a - 1.0) * cosw0) + twoSqrtAAlpha;
        double a1 = 2.0 * ((a - 1.0) - ((a + 1.0) * cosw0));
        double a2 = (a + 1.0) - ((a - 1.0) * cosw0) - twoSqrtAAlpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static (double W0, double CosW0, double Alpha) Omega(double freq, int sampleRate, double q)
    {
        // Keep the cutoff strictly inside (0, Nyquist) for a stable design.
        double nyquist = sampleRate / 2.0;
        double f = Math.Clamp(freq, 1.0, nyquist * 0.99);
        double w0 = 2.0 * Math.PI * f / sampleRate;
        double alpha = Math.Sin(w0) / (2.0 * q);
        return (w0, Math.Cos(w0), alpha);
    }

    private static BiquadCoefficients Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
        => new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
}
