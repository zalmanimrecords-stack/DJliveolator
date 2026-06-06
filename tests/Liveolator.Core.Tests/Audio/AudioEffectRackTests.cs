using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Effects;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Tests.Audio;

public sealed class AudioEffectRackTests
{
    [Fact]
    public void Process_UsesRackOrderAndBypass()
    {
        var factory = new FakeFactory();
        using var rack = new RealtimeAudioEffectRack(AudioEffectRackSlot.DeckA, factory);
        string doubleId = rack.Load("double", "double-1");
        string addId = rack.Load("add-one", "add-1");
        var buffer = new[] { 1f, 2f };

        rack.Process(buffer, channels: 1);

        Assert.Equal(new[] { 3f, 5f }, buffer);

        rack.ToggleBypass(addId);
        buffer = new[] { 1f, 2f };
        rack.Process(buffer, channels: 1);
        Assert.Equal(new[] { 2f, 4f }, buffer);
        Assert.Equal(7, rack.State.LatencySamples);
        Assert.Equal(doubleId, rack.State.Effects[0].InstanceId);
    }

    [Fact]
    public void MissingPlugin_IsPreservedAsPassThroughPlaceholder()
    {
        using var rack = new RealtimeAudioEffectRack(AudioEffectRackSlot.Master, new FakeFactory());
        string id = rack.Load("missing", "missing-1");
        var buffer = new[] { 0.25f };

        rack.Process(buffer, 1);

        Assert.Equal(0.25f, buffer[0]);
        Assert.True(rack.State.Effects.Single().IsMissing);
        Assert.Equal(id, rack.State.Effects.Single().InstanceId);
    }

    [Fact]
    public void ActionHandler_RoutesTargetAndParameterThroughDispatcher()
    {
        var factory = new FakeFactory();
        using var provider = new AudioEffectRackProvider(factory);
        var dispatcher = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new AudioEffectActionHandler(provider) },
            NullLogger<PerformanceActionDispatcher>.Instance);

        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.AudioFxLoad,
            Slot: AudioEffectRackSlot.DeckB,
            Argument: "double",
            Target: "fx-1"));
        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.AudioFxSetParameter,
            ActionInputMode.Absolute,
            Value: 0.75,
            Slot: AudioEffectRackSlot.DeckB,
            Argument: "mix",
            Target: "fx-1"));

        FakeProcessor processor = Assert.Single(factory.Created);
        Assert.Equal(0.75, processor.Parameters["mix"]);
        Assert.True(dispatcher.GetFeedback(
            PerformanceActionKind.AudioFxSetParameter, AudioEffectRackSlot.DeckB).IsAvailable);
    }

    private sealed class FakeFactory : IAudioEffectProcessorFactory
    {
        public List<FakeProcessor> Created { get; } = new();

        public bool TryCreate(string pluginUid, out IAudioEffectProcessor processor)
        {
            if (pluginUid == "missing")
            {
                processor = default!;
                return false;
            }

            var created = new FakeProcessor(pluginUid);
            Created.Add(created);
            processor = created;
            return true;
        }
    }

    private sealed class FakeProcessor(string uid) : IAudioEffectProcessor
    {
        public Dictionary<string, double> Parameters { get; } = new();
        public string PluginUid => uid;
        public int LatencySamples => 7;
        public void Dispose() { }
        public void LoadPreset(ReadOnlySpan<byte> state) { }
        public void SetParameter(string parameterId, double normalizedValue)
            => Parameters[parameterId] = normalizedValue;

        public void Process(Span<float> interleaved, int channels)
        {
            for (int i = 0; i < interleaved.Length; i++)
                interleaved[i] = uid == "double" ? interleaved[i] * 2 : interleaved[i] + 1;
        }
    }
}
