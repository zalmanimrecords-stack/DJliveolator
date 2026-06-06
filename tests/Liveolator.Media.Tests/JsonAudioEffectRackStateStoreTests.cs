using Liveolator.Core.Audio.Effects;

namespace Liveolator.Media.Tests;

public sealed class JsonAudioEffectRackStateStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "liveolator-fx-state-tests", Guid.NewGuid().ToString("N"));

    public JsonAudioEffectRackStateStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveThenLoad_PreservesOrderParametersBypassAndOpaqueState()
    {
        var store = new JsonAudioEffectRackStateStore(_root);
        var state = new AudioEffectRackState(
            AudioEffectRackSlot.DeckA,
            new[]
            {
                new AudioEffectInstanceState(
                    "fx-1",
                    "plugin-uid",
                    IsBypassed: true,
                    new Dictionary<string, double> { ["mix"] = 0.75 },
                    new byte[] { 1, 2, 3 }),
            },
            LatencySamples: 64);

        await store.SaveAsync(new[] { state });
        AudioEffectRackState loaded = Assert.Single(await store.LoadAsync());

        AudioEffectInstanceState effect = Assert.Single(loaded.Effects);
        Assert.Equal("fx-1", effect.InstanceId);
        Assert.True(effect.IsBypassed);
        Assert.Equal(0.75, effect.Parameters["mix"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, effect.OpaqueState);
    }
}
