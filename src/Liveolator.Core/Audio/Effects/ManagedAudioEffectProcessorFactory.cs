namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// Creates the built-in, in-house DSP effects (Moog ladder low-pass, reverb, phaser) by UID. The
/// <see cref="IAudioEffectProcessor"/> contract has no prepare/sample-rate hook, so the rate is baked in
/// here at construction (the composition root passes the engine's output rate).
/// </summary>
public sealed class ManagedAudioEffectProcessorFactory : IAudioEffectProcessorFactory
{
    private readonly int _sampleRate;

    public ManagedAudioEffectProcessorFactory(int sampleRate = 48_000)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _sampleRate = sampleRate;
    }

    public bool TryCreate(string pluginUid, out IAudioEffectProcessor processor)
    {
        processor = pluginUid switch
        {
            BuiltInAudioEffects.MoogUid => new MoogLadderFilterProcessor(_sampleRate),
            BuiltInAudioEffects.ReverbUid => new FreeverbProcessor(_sampleRate),
            BuiltInAudioEffects.PhaserUid => new PhaserProcessor(_sampleRate),
            _ => default!,
        };
        return processor is not null;
    }
}
