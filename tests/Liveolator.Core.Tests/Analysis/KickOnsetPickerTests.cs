using System.Linq;
using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class KickOnsetPickerTests
{
    private const double RateHz = 100.0;

    // A kick envelope: a tall spike every `periodFrames`, low ripple elsewhere.
    private static double[] KickTrain(int frames, int periodFrames, double spike, double ripple)
    {
        var e = new double[frames];
        for (int i = 0; i < frames; i++)
            e[i] = ripple;
        for (int i = 0; i < frames; i += periodFrames)
            e[i] = spike;
        return e;
    }

    [Fact]
    public void Pick_ReturnsOneTimePerKick_AtTheSpikeInstants()
    {
        // Spikes every 50 frames (0.5 s → 120 BPM) over 10 s.
        double[] envelope = KickTrain(frames: 1_000, periodFrames: 50, spike: 1.0, ripple: 0.02);

        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(envelope, RateHz);

        Assert.Equal(20, onsets.Count);
        for (int k = 0; k < onsets.Count; k++)
            Assert.Equal(k * 0.5, onsets[k], 3); // 0.0, 0.5, 1.0, ...
    }

    [Fact]
    public void Pick_AddsAnalysisLatency()
    {
        double[] envelope = KickTrain(frames: 200, periodFrames: 50, spike: 1.0, ripple: 0.0);

        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(envelope, RateHz, analysisLatencySeconds: 0.01);

        Assert.Equal(0.01, onsets[0], 4); // frame 0 + 10 ms latency
        Assert.Equal(0.51, onsets[1], 4);
    }

    [Fact]
    public void Pick_RejectsLowRippleBelowTheRelativeFloor()
    {
        // Ripple at 10% of the spike is under the 15% floor → only the spikes are picked.
        double[] envelope = KickTrain(frames: 500, periodFrames: 50, spike: 1.0, ripple: 0.10);

        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(envelope, RateHz);

        Assert.Equal(10, onsets.Count);
    }

    [Fact]
    public void Pick_DoesNotDoublePickTheDecayShoulderOfOneKick()
    {
        // One kick with a decaying tail over several frames must yield exactly one onset (refractory).
        var e = new double[300];
        e[100] = 1.0; e[101] = 0.8; e[102] = 0.6; e[103] = 0.4; e[104] = 0.25;

        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(e, RateHz);

        Assert.Equal(1.0, Assert.Single(onsets), 3); // frame 100 → 1.0 s
    }

    [Fact]
    public void Pick_EmptyOrSilent_ReturnsEmpty()
    {
        Assert.Empty(KickOnsetPicker.Pick(System.Array.Empty<double>(), RateHz));
        Assert.Empty(KickOnsetPicker.Pick(new double[500], RateHz));       // all zero
        Assert.Empty(KickOnsetPicker.Pick(new double[] { 1, 1, 1 }, 0.0)); // non-positive rate
    }
}
