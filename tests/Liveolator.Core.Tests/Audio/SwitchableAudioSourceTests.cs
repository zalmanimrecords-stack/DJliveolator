using System.Collections.Generic;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class SwitchableAudioSourceTests
{
    private static AudioSamplesAvailable Batch() => new(new float[8], 2, 48_000);

    [Fact]
    public void ForwardsSamplesFromInner()
    {
        var sw = new SwitchableAudioSource();
        var inner = new FakeAudioSource();
        var received = new List<AudioSamplesAvailable>();
        sw.SamplesAvailable += (_, e) => received.Add(e);

        sw.SetSource(inner);
        inner.Emit(new float[8], 2, 48_000);

        Assert.Single(received);
    }

    [Fact]
    public void SwitchingSource_StopsForwardingTheOldOne()
    {
        var sw = new SwitchableAudioSource();
        var first = new FakeAudioSource();
        var second = new FakeAudioSource();
        var received = new List<AudioSamplesAvailable>();
        sw.SamplesAvailable += (_, e) => received.Add(e);

        sw.SetSource(first);
        sw.SetSource(second);
        first.Emit(new float[8], 2, 48_000);  // old source — must be ignored
        second.Emit(new float[8], 2, 48_000);

        Assert.Single(received);
    }

    [Fact]
    public void SetSourceNull_Detaches()
    {
        var sw = new SwitchableAudioSource();
        var inner = new FakeAudioSource();
        var received = new List<AudioSamplesAvailable>();
        sw.SamplesAvailable += (_, e) => received.Add(e);

        sw.SetSource(inner);
        sw.SetSource(null);
        inner.Emit(new float[8], 2, 48_000);

        Assert.Empty(received);
    }

    [Fact]
    public void StartStopAndIsRunning_DelegateToInner()
    {
        var sw = new SwitchableAudioSource();
        var inner = new FakeAudioSource();
        sw.SetSource(inner);

        Assert.False(sw.IsRunning);
        sw.Start();
        Assert.True(inner.IsRunning);
        Assert.True(sw.IsRunning);
        sw.Stop();
        Assert.False(inner.IsRunning);
    }

    [Fact]
    public void Name_ReflectsInner_OrNone()
    {
        var sw = new SwitchableAudioSource();
        Assert.Equal("(none)", sw.Name);
        sw.SetSource(new FakeAudioSource());
        Assert.Equal("Fake", sw.Name);
    }

    [Fact]
    public void Dispose_StopsForwarding_ButDoesNotDisposeInner()
    {
        var sw = new SwitchableAudioSource();
        var inner = new FakeAudioSource();
        var received = new List<AudioSamplesAvailable>();
        sw.SamplesAvailable += (_, e) => received.Add(e);
        sw.SetSource(inner);

        sw.Dispose();
        inner.Emit(new float[8], 2, 48_000);

        Assert.Empty(received);
        Assert.Equal(0, inner.DisposeCount); // ownership stays with the caller
    }
}
