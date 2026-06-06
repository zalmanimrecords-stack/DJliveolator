namespace Liveolator.Core.Audio.Effects;

public static class AudioEffectRackSlot
{
    public const int DeckA = 0;
    public const int DeckB = 1;
    public const int Master = 2;
    public const int Count = 3;
}

public sealed record AudioEffectParameterDescriptor(
    string Id,
    string Name,
    double Default,
    int StepCount = 0);

public sealed record AudioEffectPluginDescriptor(
    string PluginUid,
    string Name,
    string Vendor,
    IReadOnlyList<AudioEffectParameterDescriptor> Parameters,
    int LatencySamples,
    bool IsAvailable,
    bool IsQuarantined = false);

public sealed record AudioEffectInstanceState(
    string InstanceId,
    string PluginUid,
    bool IsBypassed,
    IReadOnlyDictionary<string, double> Parameters,
    byte[]? OpaqueState,
    bool IsMissing = false);

public sealed record AudioEffectRackState(
    int Slot,
    IReadOnlyList<AudioEffectInstanceState> Effects,
    int LatencySamples);

public interface IAudioEffectProcessor : IDisposable
{
    string PluginUid { get; }
    int LatencySamples { get; }
    void SetParameter(string parameterId, double normalizedValue);
    void LoadPreset(ReadOnlySpan<byte> state);
    void Process(Span<float> interleaved, int channels);
}

public interface IAudioEffectProcessorFactory
{
    bool TryCreate(string pluginUid, out IAudioEffectProcessor processor);
}

public interface IAudioEffectPluginCatalog
{
    IReadOnlyList<AudioEffectPluginDescriptor> Plugins { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IAudioEffectRack
{
    AudioEffectRackState State { get; }
    string Load(string pluginUid, string? instanceId = null);
    void Unload(string instanceId);
    void Move(string instanceId, int index);
    void ToggleBypass(string instanceId);
    void SetParameter(string instanceId, string parameterId, double normalizedValue);
    void LoadPreset(string instanceId, ReadOnlySpan<byte> state);
    void Process(Span<float> interleaved, int channels);
}

public interface IAudioEffectRackProvider
{
    IAudioEffectRack GetRack(int slot);
}
