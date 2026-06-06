namespace Liveolator.Core.Audio;

/// <summary>
/// A two-deck playback engine seam (doc 11): the same load/play-pause/stop operations as
/// <see cref="IAudioPlaybackEngine"/> but addressed per deck slot (A = 0, B = 1), so a single
/// <see cref="DeckActionHandler"/> can drive both decks from slot-tagged actions. Kept separate
/// from the single-deck seam so the existing single-deck path and its tests are untouched; the
/// concrete two-deck engine (and its BASS routing into the software mixer) is a later increment.
/// </summary>
public interface IMultiDeckPlaybackEngine
{
    /// <summary>Number of addressable deck slots.</summary>
    int DeckCount { get; }

    /// <summary>
    /// Raised when a deck slot's track reaches its end during playback (doc 11/22 A4). The event arg is
    /// the slot index. The live-queue audio binding listens for this to auto-advance (or stop when the
    /// queue is dry). Raised off the audio binding's end-of-stream signal, so handlers must be tolerant
    /// of the calling thread; a single-deck engine adapts it to slot 0.
    /// </summary>
    event EventHandler<int>? DeckEnded;

    /// <summary>True while the given deck slot is playing.</summary>
    bool IsPlaying(int slot);

    /// <summary>Load a track into a deck slot, replacing any current one. Does not auto-play.</summary>
    void Load(int slot, string trackPath);

    /// <summary>Start the deck slot, or pause it if already playing. No-op if nothing is loaded.</summary>
    void PlayPause(int slot);

    /// <summary>Stop the deck slot (keeps its loaded track).</summary>
    void Stop(int slot);

    // --- Transport (doc 11): position scrub, pitch/tempo, cue, and per-deck sync/quantize toggles.
    // Driven via DeckSeek/DeckPitch/DeckCue/DeckSyncLockToggle/DeckQuantizeToggle actions, never directly.

    /// <summary>Current playback position as a normalized 0..1 fraction of the track (0 if nothing loaded).</summary>
    double Position(int slot);

    /// <summary>
    /// Move the playhead. When <paramref name="relative"/> is false, <paramref name="position"/> is an
    /// absolute 0..1 fraction; when true it is a signed delta added to the current position. The engine
    /// clamps the result to 0..1. No-op if nothing is loaded.
    /// </summary>
    void Seek(int slot, double position, bool relative);

    /// <summary>Normalized pitch position 0..1 where 0.5 = original tempo (the engine owns the % range).</summary>
    double PitchPosition(int slot);

    /// <summary>
    /// Adjust the deck's pitch/tempo. An absolute <paramref name="value"/> is a 0..1 position
    /// (0.5 = no change); a relative value is a signed delta. The engine maps the normalized position
    /// to its tempo range.
    /// </summary>
    void SetPitch(int slot, double value, bool relative);

    /// <summary>Jump the playhead to the deck's cue point (defaults to the track start) and pause there.</summary>
    void Cue(int slot);

    /// <summary>
    /// The deck's analyzed natural tempo (BPM) used as the Sync reference; 0 when unknown. Set when a
    /// track with a known BPM loads so Sync Lock can beatmatch against it (doc 11).
    /// </summary>
    double DeckBaseBpm(int slot);

    /// <summary>Set the deck's natural tempo (BPM) used as the Sync reference. 0 (or negative) = unknown.</summary>
    void SetDeckBaseBpm(int slot, double bpm);

    /// <summary>
    /// The deck's first-beat (downbeat) anchor in seconds from the track start — the within-beat offset
    /// where the analyzed beat grid begins. Used by Quantize/phase-match to align the deck to the shared
    /// grid (doc 11). 0 when unknown.
    /// </summary>
    double DeckFirstBeat(int slot);

    /// <summary>
    /// Set the deck's first-beat anchor (seconds), fed from the track's analyzed <c>BpmResult</c> on
    /// load (like base BPM). Negative = unknown.
    /// </summary>
    void SetDeckFirstBeat(int slot, double firstBeatSeconds);

    /// <summary>True while the deck is sync-locked (beatmatched) to the master tempo.</summary>
    bool IsSyncLocked(int slot);

    /// <summary>Enable or disable sync-lock for the deck.</summary>
    void SetSyncLock(int slot, bool enabled);

    /// <summary>True while the deck quantizes cue/loop actions to the beat grid.</summary>
    bool IsQuantizeEnabled(int slot);

    /// <summary>Enable or disable beat-quantize for the deck.</summary>
    void SetQuantize(int slot, bool enabled);

    /// <summary>Number of hot-cue slots per deck (valid <see cref="HotCue"/> indices are 0..count-1).</summary>
    int HotCueCount { get; }

    /// <summary>True if the deck's hot-cue at <paramref name="cueIndex"/> is set for the loaded track.</summary>
    bool IsHotCueSet(int slot, int cueIndex);

    /// <summary>
    /// Trigger a hot-cue: set it at the current position if unset, otherwise jump the playhead to it.
    /// Hot-cues belong to the loaded track and are cleared when a new track loads.
    /// </summary>
    void HotCue(int slot, int cueIndex);

    // --- Loops (doc 11): a beat-length loop repeats a region of the track. Driven via DeckSetLoop.

    /// <summary>The deck's active loop length in beats, or 0 when no loop is active.</summary>
    double LoopBeats(int slot);

    /// <summary>True while the deck is looping a region.</summary>
    bool IsLooping(int slot);

    /// <summary>
    /// Start a beat-length loop on the deck, beginning at the current playhead. The beat length is
    /// converted to a time region using the deck's base BPM, so it is musically <paramref name="beats"/>
    /// beats long. No-op (and feedback-only) if nothing is loaded or the base BPM is unknown.
    /// </summary>
    void SetLoop(int slot, double beats);

    /// <summary>Clear the deck's active loop (playback continues past the former loop region).</summary>
    void ClearLoop(int slot);
}
