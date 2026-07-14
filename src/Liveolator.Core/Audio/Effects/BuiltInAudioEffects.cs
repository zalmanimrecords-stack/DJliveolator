namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// Identifiers for the built-in, in-house DSP effects (Moog ladder low-pass, reverb, phaser) that the
/// channel-strip FX mode drives. The plugin UIDs are resolved by <see cref="ManagedAudioEffectProcessorFactory"/>;
/// the instance IDs are the stable per-deck rack instances the UI addresses via <c>AudioFxSetParameter</c>
/// so the composition root (which loads them) and the view-model (which turns the knobs) never drift apart.
/// </summary>
public static class BuiltInAudioEffects
{
    // Plugin UIDs — what the managed factory maps to a processor.
    public const string MoogUid = "liveolator.moog";
    public const string ReverbUid = "liveolator.reverb";
    public const string PhaserUid = "liveolator.phaser";

    // Per-deck rack instance IDs for the channel-strip FX chain (order = Moog -> Phaser -> Reverb).
    public const string MoogInstance = "deck-moog";
    public const string PhaserInstance = "deck-phaser";
    public const string ReverbInstance = "deck-reverb";

    // Parameter IDs (all normalized 0..1).
    public const string Cutoff = "cutoff";
    public const string Resonance = "resonance";
    public const string Wet = "wet";

    /// <summary>Neutral (fully dry / transparent) value for a parameter, so EQ mode can silence the FX
    /// chain: an open filter (cutoff = 1) with no resonance and no wet mix passes audio through unchanged.</summary>
    public static double Neutral(string parameterId) => parameterId == Cutoff ? 1.0 : 0.0;
}
