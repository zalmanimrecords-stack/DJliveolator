using Liveolator.Audio.Playback;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public sealed class StatefulBiquadTests
{
    [Fact]
    public void Process_AppliesTheConfiguredCoefficients_FromZeroHistory()
    {
        var biquad = new StatefulBiquad(channels: 1);
        biquad.SetCoefficients(new BiquadCoefficients(B0: 0.5, B1: 0, B2: 0, A1: 0, A2: 0));

        // History starts at zero, so y = B0 * x.
        Assert.Equal(1.0, biquad.Process(0, 2.0), precision: 9);
    }

    [Fact]
    public void SetCoefficients_TakesEffectOnTheNextSample()
    {
        var biquad = new StatefulBiquad(channels: 1);

        // The default is Bypass (B0 = 1) — the signal passes through unchanged.
        Assert.Equal(3.0, biquad.Process(0, 3.0), precision: 9);

        // Swapping the coefficients must be observed immediately by the next Process call.
        biquad.SetCoefficients(new BiquadCoefficients(B0: 0, B1: 0, B2: 0, A1: 0, A2: 0));
        Assert.Equal(0.0, biquad.Process(0, 5.0), precision: 9);
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentSetAndProcess_NeverThrows_AndStaysFinite()
    {
        // Guards the atomic-publish contract (doc 27 B1): hammering SetCoefficients from one thread while
        // Process runs on another must never throw and must always yield a finite sample — the audio
        // thread only ever sees a whole, consistent coefficient set, never a torn one.
        var biquad = new StatefulBiquad(channels: 2);
        var a = new BiquadCoefficients(B0: 0.9, B1: -0.2, B2: 0.1, A1: -0.3, A2: 0.05);
        var b = new BiquadCoefficients(B0: 0.4, B1: 0.1, B2: -0.05, A1: 0.2, A2: -0.1);

        using var done = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var writer = System.Threading.Tasks.Task.Run(() =>
        {
            int i = 0;
            while (!done.IsCancellationRequested)
                biquad.SetCoefficients((i++ & 1) == 0 ? a : b);
        });

        Exception? failure = null;
        try
        {
            int i = 0;
            while (!done.IsCancellationRequested)
            {
                double y = biquad.Process(i++ & 1, ((i & 7) - 4) / 4.0);
                Assert.True(double.IsFinite(y));
            }
        }
        catch (Exception ex) { failure = ex; }

        await writer;
        Assert.Null(failure);
    }
}
