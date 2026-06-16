using Liveolator.Core.Audio.Sync;

namespace Liveolator.Audio.Playback;

/// <summary>A deck currently plugged into the mix: its BASS handle, FX control, and play state.</summary>
internal sealed record LoadedDeck(int Handle, IBassMixerChannel Channel, bool Playing);

/// <summary>
/// Mutable per-deck transport and sync state for <see cref="TwoDeckBassEngine"/>. One instance per slot,
/// replacing the engine's parallel per-slot arrays. All access is serialized by the engine's <c>_gate</c>
/// lock; this type carries no lock of its own and performs no native I/O.
/// </summary>
internal sealed class DeckSlot
{
    public DeckSlot(int hotCueCount) => HotCues = new double?[hotCueCount];

    /// <summary>The loaded/plugged deck (handle + mixer channel + play state); null = nothing loaded.</summary>
    public LoadedDeck? Deck;

    /// <summary>File path of the loaded track; the cue-store key. null = nothing loaded.</summary>
    public string? LoadedPath;

    /// <summary>
    /// Temporary (primary) cue position as a 0..1 fraction; null = unset, so the Cue button returns to
    /// the track start. Belongs to the track — cleared on unload (A5).
    /// </summary>
    public double? TempCue;

    /// <summary>
    /// Normalized pitch-fader position (0..1, 0.5 = centre). Transport state that persists across track
    /// loads. Position itself is read live from the backend, so it is not stored here.
    /// </summary>
    public double PitchPosition;

    /// <summary>Playback-rate multiplier currently applied (or to apply once Sync releases).</summary>
    public double PlaybackRate;

    /// <summary>SYNC engaged for this deck (the slave). Persists across track loads.</summary>
    public bool SyncLocked;

    /// <summary>Quantize armed for this deck. Persists across track loads.</summary>
    public bool Quantize;

    /// <summary>
    /// Key-lock (master tempo) armed for this deck: tempo changes preserve the track's musical pitch.
    /// Persists across track loads. Phase 1 records intent only; the audible time-stretch (wrapping the
    /// deck stream in BASS_FX and switching the rate path from frequency to the tempo attribute) is the
    /// native Phase 3 piece, gated on hardware verification (docs/18, roadmap N4 / H1).
    /// </summary>
    public bool KeyLocked;

    /// <summary>Beat-lock indicator state (Off/Active/Locked/Drifting), driven by the correction loop.</summary>
    public SyncLockState SyncState;

    /// <summary>Analyzed natural tempo (BPM) used as the Sync reference; 0 = unknown. Cleared on unload.</summary>
    public double BaseBpm;

    /// <summary>First-beat (downbeat) anchor in seconds; 0 = unknown. Cleared on unload.</summary>
    public double FirstBeat;

    /// <summary>Active loop length in beats; 0 = no loop. Cleared on unload.</summary>
    public double LoopBeats;

    /// <summary>Hot-cue positions (0..1 fraction) per pad; a null entry = unset. Cleared on unload.</summary>
    public readonly double?[] HotCues;
}
