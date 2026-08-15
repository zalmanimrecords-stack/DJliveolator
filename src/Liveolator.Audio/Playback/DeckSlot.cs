using Liveolator.Core.Audio.Sync;

namespace Liveolator.Audio.Playback;

/// <summary>A deck currently plugged into the mix: its BASS handle, FX control, and play state.</summary>
internal sealed record LoadedDeck(int Handle, IBassMixerChannel Channel, bool Playing);

/// <summary>
/// One in-memory hot-cue on a deck. The position is a 0..1 fraction (resilient to the track length the
/// backend reports), paired with the cue's display metadata so a set/save round-trip never loses the
/// label, color, or "suggested" flag that auto-cue analysis assigned (the suggested → commit model,
/// owner decision 2026-06-19). A manual cue the DJ set or committed has <see cref="IsAuto"/> = false.
/// </summary>
internal readonly record struct HotCueState(double Fraction, string? Label, int? Color, bool IsAuto);

/// <summary>
/// Mutable per-deck transport and sync state for <see cref="TwoDeckBassEngine"/>. One instance per slot,
/// replacing the engine's parallel per-slot arrays. All access is serialized by the engine's <c>_gate</c>
/// lock; this type carries no lock of its own and performs no native I/O.
/// </summary>
internal sealed class DeckSlot
{
    public DeckSlot(int hotCueCount) => HotCues = new HotCueState?[hotCueCount];

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

    /// <summary>Which sync mode the latch runs in when engaged (SYNC-BEHAVIOR-SPEC §4): BeatLock =
    /// tempo + phase (the default), TempoOnly = tempo-match without touching phase. Persists across loads.</summary>
    public SyncMode SyncMode = SyncMode.BeatLock;

    /// <summary>Quantize armed for this deck. Persists across track loads.</summary>
    public bool Quantize;

    /// <summary>
    /// Key-lock (master tempo) armed for this deck: tempo changes preserve the track's musical pitch.
    /// Persists across track loads. Native: the deck stream is wrapped in BASS_FX and the rate rides the
    /// tempo attribute (pitch-preserving) when on, the frequency (vinyl) path when off — see
    /// <c>BassMixerBackend.ApplyRate</c>.
    /// </summary>
    public bool KeyLocked;

    /// <summary>Beat-lock indicator state (Off/Active/Locked/Drifting), driven by the correction loop.</summary>
    public SyncLockState SyncState;

    /// <summary>Analyzed natural tempo (BPM) used as the Sync reference; 0 = unknown. Cleared on unload.</summary>
    public double BaseBpm;

    /// <summary>First-beat (downbeat) anchor in seconds; 0 = unknown. Cleared on unload.</summary>
    public double FirstBeat;

    /// <summary>Analyzed kick strike times in source-media seconds. Cleared on unload.</summary>
    public double[] KickOnsets = Array.Empty<double>();

    /// <summary>Downbeat (bar-1 "one") anchor in seconds; 0 = unknown, so phase-match stays beat-level.
    /// Cleared on unload — a stale bar anchor must never mis-snap the next track.</summary>
    public double Downbeat;

    /// <summary>Whether the analyzed grid is trustworthy enough to PHASE-sync (SYNC-BEHAVIOR-SPEC §7). When
    /// false, Sync tempo-matches only and skips phase alignment.
    /// <para><b>Defaults FALSE, and every load resets it to false.</b> It used to default true, so a slot
    /// nobody had judged offered a confident phase lock — and the anchor it locked onto came from the
    /// pre-v12 broadband envelope that measured 37–214 ms wrong. Unknown must mean tempo-only: an
    /// unnecessary downgrade costs a DJ far less than a confident-but-wrong lock that drifts on a full
    /// floor. The load path pushes the real verdict, so a judged track is ready within the same load.</para>
    /// </summary>
    public bool PhaseSyncReady;

    /// <summary>Active loop length in beats; 0 = no loop. Cleared on unload.</summary>
    public double LoopBeats;

    /// <summary>Active loop in-point in seconds, so halve/double can resize from the fixed start rather
    /// than the live playhead. Only meaningful while <see cref="LoopBeats"/> &gt; 0.</summary>
    public double LoopStartSeconds;

    /// <summary>Hot-cue bank per pad (position + label/color/auto metadata); a null entry = unset.
    /// Cleared on unload.</summary>
    public readonly HotCueState?[] HotCues;

    /// <summary>
    /// True when the loaded track opened as a 4-stem submix (doc 32 §2b) rather than a single file — so the
    /// per-stem mute controls are live. Set on load from the stem-vs-file decision; cleared on unload.
    /// </summary>
    public bool IsStemDeck;

    /// <summary>
    /// Per-stem mute state in <see cref="Liveolator.Core.Analysis.Stems.StemSet.RequiredStems"/> order
    /// (Drums/Bass/Vocals/Other); true = muted. Belongs to the track, like the hot cues: the engine opens
    /// fresh decoders at unity on every load, so this resets to all-audible on unload.
    /// </summary>
    public readonly bool[] StemMuted = new bool[Liveolator.Core.Analysis.Stems.StemSet.RequiredStems.Count];
}
