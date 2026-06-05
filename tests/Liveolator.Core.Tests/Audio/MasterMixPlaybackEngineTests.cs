using System;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Tests.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class MasterMixPlaybackEngineTests
{
    private static MasterMixPlaybackEngine NewEngine(IAudioSource master)
        => new(master, new FakeHostClock(1_000_000));

    [Fact]
    public void NullMaster_Throws()
        => Assert.Throws<ArgumentNullException>(() => NewEngine(null!));

    [Fact]
    public void BeatClock_IsStable()
    {
        var master = new FakeAudioSource();
        using var engine = NewEngine(master);

        IBeatClock first = engine.BeatClock;
        IBeatClock second = engine.BeatClock;

        Assert.Same(first, second);
    }

    [Fact]
    public void Dispose_DoesNotDisposeMaster()
    {
        // The master mix is owned by the two-deck engine (the BASS mixer); this composition
        // only wires the pipeline + clock onto it, mirroring SwitchableAudioSource's ownership rule.
        var master = new FakeAudioSource();
        var engine = NewEngine(master);

        engine.Dispose();

        Assert.Equal(0, master.DisposeCount);
    }

    [Fact]
    public void DetectsTempo_FromMasterMix_EndToEnd()
    {
        // The whole point of the increment: the beat clock follows the post-crossfader master
        // mix, not a single switched deck. Feed a 120 BPM click through the master source.
        var master = new FakeAudioSource();
        using var engine = NewEngine(master);

        const int rate = 44_100;
        const int period = 22_050;   // impulse every 0.5 s -> 120 BPM
        const int seconds = 12;
        int total = rate * seconds;

        const int chunk = rate / 10;
        var buffer = new float[chunk];
        for (int start = 0; start < total; start += chunk)
        {
            Array.Clear(buffer);
            for (int i = 0; i < chunk; i++)
            {
                if ((start + i) % period == 0)
                    buffer[i] = 1.0f; // onset impulse
            }
            master.Emit((float[])buffer.Clone(), channels: 2, sampleRate: rate);
        }

        Assert.InRange(engine.BeatClock.Current.Bpm, 105.0, 135.0);
    }
}
