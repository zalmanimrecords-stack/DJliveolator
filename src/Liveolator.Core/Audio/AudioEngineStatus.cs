namespace Liveolator.Core.Audio;

/// <summary>
/// Startup health of the realtime audio engine, surfaced to the shell as a banner. When a native library
/// is missing the symptom is otherwise invisible — the engine constructs, the decks render, but EVERY
/// track load throws and is swallowed, so playback and SYNC silently do nothing (the owner's report). A
/// one-time self-check at composition fills this so the failure is stated up front instead.
/// </summary>
/// <param name="PlaybackAvailable">True when a realtime deck engine was built (false = no native BASS at
/// all → catalog-browser mode, no playback/SYNC).</param>
/// <param name="EffectsAvailable">True when the BASS_FX (tempo/key-lock) library loads. False means tracks
/// cannot load (every <c>BassFx.TempoCreate</c> throws) even though the engine exists.</param>
/// <param name="Warning">A short, user-facing reason when something is wrong; null when healthy.</param>
public sealed record AudioEngineStatus(bool PlaybackAvailable, bool EffectsAvailable, string? Warning)
{
    /// <summary>The all-clear status (realtime playback + effects both available).</summary>
    public static AudioEngineStatus Healthy { get; } = new(PlaybackAvailable: true, EffectsAvailable: true, Warning: null);

    /// <summary>True when there is a warning worth showing the user.</summary>
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}
