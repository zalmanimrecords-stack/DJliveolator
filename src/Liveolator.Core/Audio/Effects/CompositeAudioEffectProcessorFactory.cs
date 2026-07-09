namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// Tries a list of <see cref="IAudioEffectProcessorFactory"/> in order and returns the first that can
/// create the requested UID. Lets the built-in managed effects (<see cref="ManagedAudioEffectProcessorFactory"/>)
/// coexist with external plugin hosts (e.g. VST3) behind the one factory the rack takes.
/// </summary>
public sealed class CompositeAudioEffectProcessorFactory : IAudioEffectProcessorFactory
{
    private readonly IReadOnlyList<IAudioEffectProcessorFactory> _factories;

    public CompositeAudioEffectProcessorFactory(params IAudioEffectProcessorFactory[] factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _factories = factories;
    }

    public bool TryCreate(string pluginUid, out IAudioEffectProcessor processor)
    {
        foreach (IAudioEffectProcessorFactory factory in _factories)
            if (factory.TryCreate(pluginUid, out processor))
                return true;
        processor = default!;
        return false;
    }
}
