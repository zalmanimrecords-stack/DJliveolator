using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Bpm;

/// <summary>
/// The low-band anchor lands on the kick's BODY, which arrives a kick-design-dependent time after the
/// click the ear actually aligns. Measured on two built sets (2026-10-31): the published grid sat 14-50 ms
/// after the >2 kHz click on every track, so joins flammed 15-24 ms. These pin the refinement that moves
/// the anchor onto the click — and leaves it alone when there is no click to move to.
/// </summary>
public sealed class KickClickAnchorTests
{
    private const int SampleRate = 44_100;
    private const double Bpm = 145.0;
    private const double ClickSeconds = 0.200;

    [Fact]
    public void Offset_AnchorLateOnTheBody_MovesItBackOntoTheClick()
    {
        float[] signal = Kicks(withClick: true, bodyDelaySeconds: 0.030);

        double offset = KickClickAnchor.Offset(signal, SampleRate, Bpm, anchorSeconds: ClickSeconds + 0.030);

        Assert.InRange(offset, -0.031, -0.029);
    }

    [Fact]
    public void Offset_NoClick_LeavesTheAnchorAlone()
    {
        float[] signal = Kicks(withClick: false, bodyDelaySeconds: 0.0);

        Assert.Equal(0.0, KickClickAnchor.Offset(signal, SampleRate, Bpm, anchorSeconds: ClickSeconds));
    }

    [Fact]
    public void Offset_LoudOffbeatHat_IsNotMistakenForTheClick()
    {
        float[] signal = Kicks(withClick: true, bodyDelaySeconds: 0.020, offbeatHatAmplitude: 1.0);

        double offset = KickClickAnchor.Offset(signal, SampleRate, Bpm, anchorSeconds: ClickSeconds + 0.020);

        Assert.InRange(offset, -0.021, -0.019);
    }

    [Fact]
    public void Offset_ClickFarBeforeTheBody_IsStillFound()
    {
        // The earliest click measured on a real set sat 87 ms before the low-band anchor.
        float[] signal = Kicks(withClick: true, bodyDelaySeconds: 0.085);

        double offset = KickClickAnchor.Offset(signal, SampleRate, Bpm, anchorSeconds: ClickSeconds + 0.085);

        Assert.InRange(offset, -0.086, -0.084);
    }

    [Fact]
    public void Offset_LoudSixteenthHatBeforeTheBeat_IsNotMistakenForTheClick()
    {
        float[] signal = Kicks(withClick: true, bodyDelaySeconds: 0.020, sixteenthHatAmplitude: 1.0);

        double offset = KickClickAnchor.Offset(signal, SampleRate, Bpm, anchorSeconds: ClickSeconds + 0.020);

        Assert.InRange(offset, -0.021, -0.019);
    }

    [Fact]
    public void Detect_TwoKickDesigns_PublishTheSameClickPhase()
    {
        // The flam in a built set is the DIFFERENCE between two tracks' anchors. A kick whose body swells in
        // late must not publish a later beat than a punchy one when both clicks land on the same instant.
        double period = 60.0 / Bpm;
        double punchy = new BpmDetector().Detect(Kicks(withClick: true, bodyDelaySeconds: 0.0), SampleRate).FirstBeatSeconds;
        double swelling = new BpmDetector().Detect(Kicks(withClick: true, bodyDelaySeconds: 0.035), SampleRate).FirstBeatSeconds;

        Assert.True(
            BeatMixSignals.CircularDistanceSeconds(punchy, swelling, period) <= 0.003,
            $"punchy kick anchored at {punchy:F4}s, swelling kick at {swelling:F4}s");
        Assert.True(
            BeatMixSignals.CircularDistanceSeconds(swelling, ClickSeconds, period) <= 0.003,
            $"swelling kick anchored at {swelling:F4}s, its click is at {ClickSeconds:F4}s");
    }

    // A four-on-the-floor of psy-style kicks: an optional 1 ms broadband click on the beat, then a 55 Hz body
    // that starts bodyDelaySeconds later, plus a bright tick on every off-beat and, optionally, one a 16th note
    // before every beat.
    private static float[] Kicks(
        bool withClick, double bodyDelaySeconds, double offbeatHatAmplitude = 0.5, double sixteenthHatAmplitude = 0.0)
    {
        var buffer = new float[SampleRate * 40];
        double period = 60.0 / Bpm;
        var random = new Random(7);
        for (double beat = ClickSeconds; beat * SampleRate < buffer.Length; beat += period)
        {
            int click = (int)Math.Round(beat * SampleRate);
            if (withClick)
            {
                for (int i = 0; i < SampleRate / 1000 && click + i < buffer.Length; i++)
                    buffer[click + i] += (float)(0.6 * (random.NextDouble() * 2.0 - 1.0));
            }

            int body = click + (int)Math.Round(bodyDelaySeconds * SampleRate);
            for (int i = 0; i < (int)(0.15 * SampleRate) && body + i < buffer.Length; i++)
                buffer[body + i] += (float)(Math.Exp(-i / (0.05 * SampleRate)) * Math.Sin(2.0 * Math.PI * 55.0 * i / SampleRate));

            AddHat(buffer, click + (int)Math.Round(period / 2.0 * SampleRate), offbeatHatAmplitude);
            AddHat(buffer, click - (int)Math.Round(period / 4.0 * SampleRate), sixteenthHatAmplitude);
        }

        return buffer;
    }

    private static void AddHat(float[] buffer, int start, double amplitude)
    {
        if (amplitude <= 0.0 || start < 0)
            return;
        for (int i = 0; i < (int)(0.01 * SampleRate) && start + i < buffer.Length; i++)
            buffer[start + i] += (float)(amplitude * Math.Exp(-i / (0.002 * SampleRate)) * Math.Sin(2.0 * Math.PI * 9000.0 * i / SampleRate));
    }
}
