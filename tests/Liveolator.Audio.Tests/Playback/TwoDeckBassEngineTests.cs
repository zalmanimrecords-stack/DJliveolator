using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class TwoDeckBassEngineTests
{
    /// <summary>Fixed host clock — the beat clock derives BPM from frame timestamps, not host time.</summary>
    private sealed class FixedHostClock : IHostClock
    {
        public long TicksPerSecond => 1_000_000;
        public long NowTicks => 0;
    }

    private static TwoDeckBassEngine NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer)
    {
        backend = new FakeBassMixerBackend();
        mixer = new BassMixer(deckCount: TwoDeckBassEngine.Decks);
        return new TwoDeckBassEngine(backend, mixer);
    }

    [Fact]
    public void DeckCount_IsTwo()
    {
        using var engine = NewEngine(out _, out _);
        Assert.Equal(2, engine.DeckCount);
    }

    [Fact]
    public void Ctor_ArmsMasterTapOnce()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        Assert.Equal(1, backend.MasterStarts);
    }

    [Fact]
    public void MixerTooSmall_Throws()
    {
        var backend = new FakeBassMixerBackend();
        Assert.Throws<ArgumentException>(() => new TwoDeckBassEngine(backend, new BassMixer(deckCount: 1)));
    }

    [Fact]
    public void Load_OpensStreamAndRegistersChannelIntoMixer()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);

        engine.Load(1, @"C:\b.wav");

        Assert.Contains(@"C:\b.wav", backend.Opened);
        // The channel plugged for slot 1 is now the one BassMixer routes to — prove the missing seam.
        mixer.SetDeckGain(1, 0.5);
        Assert.Equal(0.5, backend.Channels[100].Volume);
    }

    [Fact]
    public void MixerActions_RouteToTheLoadedDeckChannel_EndToEnd()
    {
        // The Core handler computes the math; the engine registered the channel; BASS_FX gets it.
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);
        engine.Load(0, @"C:\a.wav");
        var handler = new MixerActionHandler(mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: -1.0)); // full deck A

        Assert.Equal(1.0, backend.Channels[100].Volume!.Value, 6);
    }

    [Fact]
    public void Load_Twice_UnplugsPreviousDeckAndClearsItsChannel()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);

        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.Load(0, @"C:\b.wav"); // handle 101

        Assert.Contains(100, backend.Unplugged);
        // After replacement, slot 0 routes to the new channel (handle 101), not the old one.
        mixer.SetDeckGain(0, 0.25);
        Assert.Null(backend.Channels[100].Volume);
        Assert.Equal(0.25, backend.Channels[101].Volume);
    }

    [Fact]
    public void PlayPause_TogglesIsPlaying()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");

        Assert.False(engine.IsPlaying(0));
        engine.PlayPause(0);
        Assert.True(engine.IsPlaying(0));
        engine.PlayPause(0);
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void PlayPause_IsPerSlot()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");

        engine.PlayPause(0);

        Assert.True(engine.IsPlaying(0));
        Assert.False(engine.IsPlaying(1));
    }

    [Fact]
    public void PlayPause_NothingLoaded_IsNoOp()
    {
        using var engine = NewEngine(out _, out _);

        engine.PlayPause(0);

        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void Stop_StopsDeck()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.PlayPause(0);

        engine.Stop(0);

        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void Load_EmptyPath_Throws()
    {
        using var engine = NewEngine(out _, out _);
        Assert.Throws<ArgumentException>(() => engine.Load(0, "  "));
    }

    [Fact]
    public void OutOfRangeSlot_Throws()
    {
        using var engine = NewEngine(out _, out _);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Load(2, @"C:\a.wav"));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.PlayPause(-1));
    }

    [Fact]
    public void Dispose_UnplugsAllDecksAndDisposesBackend()
    {
        var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");

        engine.Dispose();

        Assert.Contains(100, backend.Unplugged);
        Assert.Contains(101, backend.Unplugged);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public void MasterMix_FeedsBeatClock_EndToEnd()
    {
        // The spine of the increment: the master tap -> MasterMixPlaybackEngine -> beat clock.
        // A 120 BPM click pushed through the master is detected by the live clock.
        const int rate = 44_100;
        const int period = 22_050;   // impulse every 0.5 s -> 120 BPM
        const int seconds = 12;
        int total = rate * seconds;

        // The master format is read in the engine ctor, so set the rate before constructing it.
        var backend = new FakeBassMixerBackend { MasterInfo = new MasterMixInfo(Channels: 2, SampleRate: rate) };
        using var engine = new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
        using var playback = new MasterMixPlaybackEngine(engine.MasterSource, new FixedHostClock());

        const int chunk = rate / 10;
        var buffer = new float[chunk * 2]; // stereo interleaved
        for (int start = 0; start < total; start += chunk)
        {
            Array.Clear(buffer);
            for (int i = 0; i < chunk; i++)
            {
                if ((start + i) % period == 0)
                {
                    buffer[(i * 2)] = 1.0f;     // L
                    buffer[(i * 2) + 1] = 1.0f; // R
                }
            }
            backend.EmitMaster((float[])buffer.Clone());
        }

        Assert.InRange(playback.BeatClock.Current.Bpm, 105.0, 135.0);
    }
}
