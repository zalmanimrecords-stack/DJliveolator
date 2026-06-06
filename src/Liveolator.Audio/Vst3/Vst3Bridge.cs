using Liveolator.Core.Audio.Effects;

namespace Liveolator.Audio.Vst3;

/// <summary>
/// Stable managed boundary for the future Steinberg-SDK C ABI bridge. Implementations own all
/// native handles; this assembly never attempts to load a VST3 module directly.
/// </summary>
public interface IVst3NativeBridge
{
    bool TryCreateProcessor(string pluginUid, out IAudioEffectProcessor processor);
}

public sealed class Vst3AudioEffectProcessorFactory : IAudioEffectProcessorFactory
{
    private readonly IVst3NativeBridge? _bridge;

    public Vst3AudioEffectProcessorFactory(IVst3NativeBridge? bridge = null) => _bridge = bridge;

    public bool TryCreate(string pluginUid, out IAudioEffectProcessor processor)
    {
        if (_bridge is not null && _bridge.TryCreateProcessor(pluginUid, out processor))
            return true;
        processor = default!;
        return false;
    }
}
