using System;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Mixer;

public class MixerMathTests
{
    private const double Tol = 1e-9;

    // --- Crossfader gain mapping ---

    [Fact]
    public void Crossfader_FullLeft_SilencesDeckB()
    {
        (double a, double b) = MixerMath.CrossfaderGains(position: 0.0, CrossfaderCurve.Linear);

        Assert.Equal(1.0, a, Tol);
        Assert.Equal(0.0, b, Tol);
    }

    [Fact]
    public void Crossfader_FullRight_SilencesDeckA()
    {
        (double a, double b) = MixerMath.CrossfaderGains(position: 1.0, CrossfaderCurve.Linear);

        Assert.Equal(0.0, a, Tol);
        Assert.Equal(1.0, b, Tol);
    }

    [Fact]
    public void Crossfader_Linear_Center_IsHalfEach()
    {
        (double a, double b) = MixerMath.CrossfaderGains(position: 0.5, CrossfaderCurve.Linear);

        Assert.Equal(0.5, a, Tol);
        Assert.Equal(0.5, b, Tol);
    }

    [Fact]
    public void Crossfader_Smooth_Center_IsConstantPower()
    {
        (double a, double b) = MixerMath.CrossfaderGains(position: 0.5, CrossfaderCurve.Smooth);

        // Constant power: each deck at cos/sin(45deg) ~= 0.7071; powers sum to 1.
        Assert.Equal(Math.Sqrt(0.5), a, 1e-6);
        Assert.Equal(Math.Sqrt(0.5), b, 1e-6);
        Assert.Equal(1.0, (a * a) + (b * b), 1e-6);
    }

    [Fact]
    public void Crossfader_Sharp_StaysNearFullUntilPastHalf()
    {
        // At a quarter toward B, the sharp curve keeps A near full and B near silent.
        (double a, double b) = MixerMath.CrossfaderGains(position: 0.25, CrossfaderCurve.Sharp);

        Assert.True(a > 0.9, $"deck A should stay loud on sharp curve, was {a}");
        Assert.True(b < 0.2, $"deck B should stay quiet on sharp curve, was {b}");
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void Crossfader_PositionIsClamped(double position)
    {
        (double a, double b) = MixerMath.CrossfaderGains(position, CrossfaderCurve.Linear);

        Assert.InRange(a, 0.0, 1.0);
        Assert.InRange(b, 0.0, 1.0);
    }

    // --- Combined per-deck output gain (channel gain * crossfader) ---

    [Fact]
    public void DeckOutputGain_CombinesChannelGainAndCrossfader()
    {
        var state = MixerState.Default
            .WithCrossfader(0.0)                                   // full deck A
            .WithChannel(MixerState.DeckA, DeckChannelState.Default with { Gain = 0.5 });

        double gainA = MixerMath.DeckOutputGain(state, MixerState.DeckA);
        double gainB = MixerMath.DeckOutputGain(state, MixerState.DeckB);

        Assert.Equal(0.5, gainA, Tol);  // 0.5 channel * 1.0 crossfader
        Assert.Equal(0.0, gainB, Tol);  // crossfader fully off B
    }

    [Theory]
    [InlineData(MixerState.DeckC)]
    [InlineData(MixerState.DeckD)]
    public void DeckOutputGain_HiddenDecks_IgnoreCrossfader(int slot)
    {
        // The crossfader blends only the live A/B decks; a hidden STUDIO deck's output is its channel
        // gain at unity crossfader factor, regardless of fader position. Verify at both extremes.
        var atA = MixerState.Default.WithCrossfader(0.0).WithChannel(slot, DeckChannelState.Default with { Gain = 0.7 });
        var atB = MixerState.Default.WithCrossfader(1.0).WithChannel(slot, DeckChannelState.Default with { Gain = 0.7 });

        Assert.Equal(0.7, MixerMath.DeckOutputGain(atA, slot), Tol);
        Assert.Equal(0.7, MixerMath.DeckOutputGain(atB, slot), Tol);
    }

    // --- EQ band coefficient design ---

    [Fact]
    public void EqBand_Flat_IsBypass()
    {
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, EqBands.Flat, sampleRate: 48_000);

        Assert.Equal(BiquadCoefficients.Bypass, c);
    }

    [Fact]
    public void EqBand_Boost_HasUnityGainPositive()
    {
        // A boosted low shelf should amplify a DC (0 Hz) signal: steady-state gain > 1.
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, new EqBands(0.9, 0.5, 0.5), 48_000);
        double dcGain = SteadyStateGain(c);

        Assert.True(dcGain > 1.0, $"boosted low band should raise DC gain, was {dcGain}");
    }

    [Fact]
    public void EqBand_Cut_HasReducedGain()
    {
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, new EqBands(0.1, 0.5, 0.5), 48_000);
        double dcGain = SteadyStateGain(c);

        Assert.True(dcGain < 1.0, $"cut low band should lower DC gain, was {dcGain}");
    }

    [Theory]
    [InlineData(EqBand.Low, 100.0)]
    [InlineData(EqBand.Mid, 1_000.0)]
    [InlineData(EqBand.High, 8_000.0)]
    public void EqBand_FullCut_IsARealKill(EqBand band, double frequency)
    {
        var killed = band switch
        {
            EqBand.Low => new EqBands(0.0, 0.5, 0.5),
            EqBand.Mid => new EqBands(0.5, 0.0, 0.5),
            _ => new EqBands(0.5, 0.5, 0.0),
        };

        BiquadCoefficients c = MixerMath.EqBandCoefficients(band, killed, 48_000);
        double gain = FrequencyGain(c, frequency, 48_000);

        Assert.True(gain <= Math.Pow(10.0, -48.0 / 20.0),
            $"{band} kill should attenuate at least 48 dB at {frequency} Hz, gain was {gain}");
    }

    // --- EQ cut-depth mode (global) ---

    [Theory]
    [InlineData(EqCutMode.Eq, -12.0)]
    [InlineData(EqCutMode.Deep, -24.0)]
    public void EqBand_GentleCutMode_FloorsTheCut_NotAKill(EqCutMode mode, double expectedFloorDb)
    {
        // Low band fully down: a gentle mode floors the cut at its depth instead of snapping to silence.
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, new EqBands(0.0, 0.5, 0.5), 48_000, mode);
        double dcDb = 20.0 * Math.Log10(SteadyStateGain(c));

        Assert.Equal(expectedFloorDb, dcDb, 2.0);                  // within 2 dB of the mode floor
        Assert.True(dcDb > -48.0, $"{mode} must not be a full kill, was {dcDb:F1} dB");
    }

    [Fact]
    public void EqBand_KillCutMode_StillKillsAtBottom()
    {
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, new EqBands(0.0, 0.5, 0.5), 48_000, EqCutMode.Kill);

        Assert.True(SteadyStateGain(c) <= Math.Pow(10.0, -48.0 / 20.0), "Kill mode must reach silence at the bottom");
    }

    [Fact]
    public void EqBand_DefaultCutMode_PreservesFullKill()
    {
        // Omitting the mode keeps the historical full-kill-at-bottom behaviour (backward compatible).
        BiquadCoefficients c = MixerMath.EqBandCoefficients(EqBand.Low, new EqBands(0.0, 0.5, 0.5), 48_000);

        Assert.True(SteadyStateGain(c) <= Math.Pow(10.0, -48.0 / 20.0));
    }

    [Fact]
    public void EqBand_CoarserCutMode_AttenuatesAtLeastAsMuch()
    {
        var eq = new EqBands(0.1, 0.5, 0.5); // a partial low cut
        double Gain(EqCutMode m) => SteadyStateGain(MixerMath.EqBandCoefficients(EqBand.Low, eq, 48_000, m));

        Assert.True(Gain(EqCutMode.Eq) > Gain(EqCutMode.Deep), "Deep should cut more than EQ");
        Assert.True(Gain(EqCutMode.Deep) >= Gain(EqCutMode.Kill), "Kill should cut at least as deep as Deep");
    }

    [Fact]
    public void EqBand_CutMode_LeavesBoostUntouched()
    {
        // The mode only governs the cut half; a boosted band is identical across modes.
        var boosted = new EqBands(0.9, 0.5, 0.5);
        BiquadCoefficients eqMode = MixerMath.EqBandCoefficients(EqBand.Low, boosted, 48_000, EqCutMode.Eq);
        BiquadCoefficients killMode = MixerMath.EqBandCoefficients(EqBand.Low, boosted, 48_000, EqCutMode.Kill);

        Assert.Equal(killMode, eqMode);
    }

    // --- Filter coefficient design ---

    [Fact]
    public void Filter_Center_IsBypass()
    {
        BiquadCoefficients c = MixerMath.FilterCoefficients(DeckChannelState.FilterCenter, 48_000);

        Assert.Equal(BiquadCoefficients.Bypass, c);
    }

    [Fact]
    public void Filter_LowPass_AttenuatesNyquist()
    {
        // Knob below center = low-pass: high frequencies (Nyquist) should be attenuated vs DC.
        BiquadCoefficients c = MixerMath.FilterCoefficients(0.1, 48_000);

        Assert.True(NyquistGain(c) < SteadyStateGain(c),
            "low-pass should pass DC more than Nyquist");
    }

    [Fact]
    public void Filter_HighPass_AttenuatesDc()
    {
        // Knob above center = high-pass: DC should be attenuated vs Nyquist.
        BiquadCoefficients c = MixerMath.FilterCoefficients(0.9, 48_000);

        Assert.True(SteadyStateGain(c) < NyquistGain(c),
            "high-pass should pass Nyquist more than DC");
    }

    [Fact]
    public void Coefficients_AreStable()
    {
        // Stability: feeding an impulse must not blow up. Check a range of settings.
        foreach (double knob in new[] { 0.0, 0.1, 0.3, 0.7, 0.9, 1.0 })
        {
            BiquadCoefficients c = MixerMath.FilterCoefficients(knob, 48_000);
            Assert.True(ImpulseResponseDecays(c), $"filter at knob {knob} must be stable");
        }
    }

    // --- helpers: evaluate a biquad's response without native DSP ---

    // Steady-state response to a constant (DC, z=1): sum(b)/(1+sum(a)).
    private static double SteadyStateGain(BiquadCoefficients c)
        => (c.B0 + c.B1 + c.B2) / (1.0 + c.A1 + c.A2);

    // Response at Nyquist (alternating ±1, z=-1): (b0-b1+b2)/(1-a1+a2).
    private static double NyquistGain(BiquadCoefficients c)
        => Math.Abs((c.B0 - c.B1 + c.B2) / (1.0 - c.A1 + c.A2));

    private static double FrequencyGain(BiquadCoefficients c, double frequency, int sampleRate)
    {
        double w = 2.0 * Math.PI * frequency / sampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1.0, -w);
        var z2 = z1 * z1;
        System.Numerics.Complex numerator = c.B0 + (c.B1 * z1) + (c.B2 * z2);
        System.Numerics.Complex denominator = 1.0 + (c.A1 * z1) + (c.A2 * z2);
        return (numerator / denominator).Magnitude;
    }

    private static bool ImpulseResponseDecays(BiquadCoefficients c)
    {
        double y1 = 0, y2 = 0, x1 = 0, x2 = 0;
        double maxLate = 0;
        for (int n = 0; n < 2000; n++)
        {
            double x = n == 0 ? 1.0 : 0.0;
            double y = c.Process(x, x1, x2, y1, y2);
            if (double.IsNaN(y) || double.IsInfinity(y))
                return false;
            if (n > 1000)
                maxLate = Math.Max(maxLate, Math.Abs(y));
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
        }
        return maxLate < 0.5; // settled well below the unit impulse
    }
}
