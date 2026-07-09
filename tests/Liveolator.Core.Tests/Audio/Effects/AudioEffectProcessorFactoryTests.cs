using Liveolator.Core.Audio.Effects;

namespace Liveolator.Core.Tests.Audio.Effects;

public sealed class AudioEffectProcessorFactoryTests
{
    [Theory]
    [InlineData(BuiltInAudioEffects.MoogUid)]
    [InlineData(BuiltInAudioEffects.ReverbUid)]
    [InlineData(BuiltInAudioEffects.PhaserUid)]
    public void Managed_CreatesEachBuiltInEffectWithMatchingUid(string uid)
    {
        var factory = new ManagedAudioEffectProcessorFactory(48_000);

        Assert.True(factory.TryCreate(uid, out IAudioEffectProcessor processor));
        Assert.Equal(uid, processor.PluginUid);
    }

    [Fact]
    public void Managed_UnknownUid_ReturnsFalse()
    {
        var factory = new ManagedAudioEffectProcessorFactory(48_000);

        Assert.False(factory.TryCreate("com.example.unknown", out IAudioEffectProcessor processor));
        Assert.Null(processor);
    }

    [Fact]
    public void Composite_TriesFactoriesInOrder_AndFallsThrough()
    {
        var composite = new CompositeAudioEffectProcessorFactory(
            new ManagedAudioEffectProcessorFactory(48_000),
            new StubFactory("com.example.vst"));

        Assert.True(composite.TryCreate(BuiltInAudioEffects.MoogUid, out IAudioEffectProcessor managed));
        Assert.Equal(BuiltInAudioEffects.MoogUid, managed.PluginUid);

        Assert.True(composite.TryCreate("com.example.vst", out IAudioEffectProcessor external));
        Assert.Equal("com.example.vst", external.PluginUid);

        Assert.False(composite.TryCreate("com.example.missing", out _));
    }

    private sealed class StubFactory(string uid) : IAudioEffectProcessorFactory
    {
        public bool TryCreate(string pluginUid, out IAudioEffectProcessor processor)
        {
            if (pluginUid == uid)
            {
                processor = new StubProcessor(uid);
                return true;
            }
            processor = default!;
            return false;
        }
    }

    private sealed class StubProcessor(string uid) : IAudioEffectProcessor
    {
        public string PluginUid => uid;
        public int LatencySamples => 0;
        public void SetParameter(string parameterId, double normalizedValue) { }
        public void LoadPreset(ReadOnlySpan<byte> state) { }
        public void Process(Span<float> interleaved, int channels) { }
        public void Dispose() { }
    }
}
