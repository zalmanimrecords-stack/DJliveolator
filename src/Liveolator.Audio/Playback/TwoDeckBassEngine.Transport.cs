using Liveolator.Core.Analysis.Stems;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Transport surface of <see cref="TwoDeckBassEngine"/>: load/unload a deck, play/pause/stop, read and
/// move the playhead (seek/jog), the CDJ cue button, and the end-of-track handoff.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public bool IsPlaying(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].Deck?.Playing ?? false;
    }

    public void Load(int slot, string trackPath)
    {
        ValidateSlot(slot);
        if (string.IsNullOrWhiteSpace(trackPath))
            throw new ArgumentException("trackPath must be a non-empty path.", nameof(trackPath));

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TwoDeckBassEngine));

            DeckSlot s = _slots[slot];
            try
            {
                // Open the new stream BEFORE unloading the current track, so a failed open (missing /
                // corrupt / unreadable file — e.g. a stale live-queue or restored entry) leaves the deck's
                // existing track loaded and playable rather than wiping it. A bad track must never empty a
                // good deck (global standards #16/#26). With the stems gate on and a complete local stem
                // set cached, this opens a 4-stem submix deck; otherwise the single file (doc 32 §2b).
                int handle = OpenDeckHandle(trackPath, out bool isStemDeck);
                UnloadSlot(slot);
                IBassMixerChannel channel = _backend.PlugDeck(handle, slot);
                _mixer.SetChannel(slot, channel); // route the Core mixer's gain/EQ/filter to this deck
                s.Deck = new LoadedDeck(handle, channel, Playing: false);
                s.LoadedPath = trackPath; // the cue-store key for this slot
                s.IsStemDeck = isStemDeck; // enables the per-stem mute controls for this deck (doc 32 §2b)
                // Re-arm key-lock on the fresh stream FIRST so the rate below takes the right audible path
                // (pitch-preserving tempo when locked, vinyl frequency otherwise). Key-lock is per-deck
                // transport state that persists across loads, like the pitch fader.
                _backend.SetDeckKeyLock(handle, s.KeyLocked);
                // Re-apply the slot's tempo to the new track so swapping decks keeps the setting: the
                // manual pitch fader normally, or the synced rate when Sync is engaged (set once the
                // load action supplies the new track's base BPM via SetDeckBaseBpm).
                if (!s.SyncLocked)
                    _backend.SetDeckRate(handle, s.PlaybackRate);
                // Restore the track's persisted hot cues (A3). Tolerant: a missing/unreadable store
                // leaves the slot with the fresh (empty) cue bank UnloadSlot cleared — never a throw.
                LoadPersistedHotCues(slot, handle, trackPath);
                // Arm end-of-track handling (A4): when this stream runs out, mark the slot stopped and
                // raise DeckEnded so the live queue can auto-advance (or stop when dry).
                _backend.SetDeckEndCallback(handle, () => OnDeckEnded(slot, handle));
                _logger.LogInformation("Loaded deck slot {Slot} <- {Track}", slot, trackPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load deck slot {Slot} <- {Track}", slot, trackPath);
                throw;
            }
        }
    }

    public void PlayPause(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_slots[slot].Deck is not { } deck)
            {
                _logger.LogWarning("PlayPause deck slot {Slot} requested with no track loaded; ignoring.", slot);
                return;
            }
            bool next = !deck.Playing;
            // Armed start (SYNC-BEHAVIOR-SPEC §6): a synced deck that was armed while stopped re-aligns its
            // beat/bar phase to the master at the moment it starts, so it enters IN PHASE no matter how long
            // it sat armed (the master kept moving). Done BEFORE the play toggle so PhaseAlignToLeader still
            // sees the deck as not-playing and can bar-snap onto the master's "one". BeatLock only — Tempo
            // Sync never touches phase; the grid-confidence gate inside PhaseAlignToLeader still applies.
            if (next && _slots[slot].SyncLocked && _slots[slot].SyncMode == SyncMode.BeatLock)
                PhaseAlignToLeader(slot);
            _backend.SetDeckPlaying(deck.Handle, next);
            _slots[slot].Deck = deck with { Playing = next };
            ReapplySyncedFollowers();
        }
        FlushSyncTransitions();
    }

    public void Stop(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_slots[slot].Deck is { Playing: true } deck)
            {
                _backend.SetDeckPlaying(deck.Handle, false);
                _slots[slot].Deck = deck with { Playing = false };
                ReapplySyncedFollowers();
            }
        }
        FlushSyncTransitions();
    }

    public double Position(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
            return _slots[slot].Deck is { } deck ? _backend.GetDeckPositionFraction(deck.Handle) : 0.0;
    }

    public double LengthSeconds(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
            return _slots[slot].Deck is { } deck
                ? Math.Max(0.0, _backend.GetDeckLengthSeconds(deck.Handle))
                : 0.0;
    }

    public void Seek(int slot, double position, bool relative)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_slots[slot].Deck is not { } deck)
                return; // nothing loaded — no playhead to move
            double target = relative
                ? Math.Clamp(_backend.GetDeckPositionFraction(deck.Handle) + position, 0.0, 1.0)
                : Math.Clamp(position, 0.0, 1.0);
            _backend.SetDeckPositionFraction(deck.Handle, target);
        }
    }

    public void Jog(int slot, double deltaSeconds)
    {
        ValidateSlot(slot);
        if (!double.IsFinite(deltaSeconds))
            return;

        lock (_gate)
        {
            if (_slots[slot].Deck is not { } deck)
                return;

            double lengthSeconds = _backend.GetDeckLengthSeconds(deck.Handle);
            if (lengthSeconds <= 0.0)
                return;

            double targetSeconds = Math.Clamp(
                _backend.GetDeckPositionSeconds(deck.Handle) + deltaSeconds,
                0.0,
                lengthSeconds);
            _backend.SetDeckPositionFraction(deck.Handle, targetSeconds / lengthSeconds);
        }
    }

    public void Cue(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck)
                return;

            // CDJ back-to-cue (A5): the pure resolver decides set-vs-return from the deck's transport
            // state, live position, and stored temp cue. Set drops a fresh cue here; return jumps to the
            // stored cue (or track start when none is set) and pauses.
            double current = _backend.GetDeckPositionFraction(deck.Handle);
            CueButtonAction action = CueButtonResolver.Resolve(deck.Playing, current, s.TempCue);
            if (action == CueButtonAction.SetCueHere)
            {
                s.TempCue = current;
                _backend.SetDeckPlaying(deck.Handle, false);
                s.Deck = deck with { Playing = false };
                _logger.LogInformation("Deck slot {Slot} cue: set temp cue at {Pos:F4}.", slot, current);
                return;
            }

            double target = s.TempCue ?? 0.0; // return to the stored cue, else the track start
            _backend.SetDeckPositionFraction(deck.Handle, target);
            _backend.SetDeckPlaying(deck.Handle, false);
            s.Deck = deck with { Playing = false };
        }
    }

    public void CuePlay(int slot, bool isPressed)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck)
                return;

            if (isPressed)
            {
                // Set the cue at the current position if none exists, then play from the cue while held —
                // the CDJ cue-play preview.
                s.TempCue ??= _backend.GetDeckPositionFraction(deck.Handle);
                _backend.SetDeckPositionFraction(deck.Handle, s.TempCue.Value);
                _backend.SetDeckPlaying(deck.Handle, true);
                s.Deck = deck with { Playing = true };
            }
            else
            {
                // Release: snap back to the cue and pause (the preview never advances the cue point).
                _backend.SetDeckPositionFraction(deck.Handle, s.TempCue ?? 0.0);
                _backend.SetDeckPlaying(deck.Handle, false);
                s.Deck = deck with { Playing = false };
            }
        }
    }

    // Caller holds _gate. Decide single-file vs 4-stem submix for this track and open the deck handle
    // (doc 32 §2b). Stems are used only when the gate is on AND a complete local stem set is cached; a
    // corrupt/unopenable stem set must never take down a deck, so a stem-open failure falls back ONCE to
    // the single mixed file before surfacing. Returns the raw deck handle PlugDeck wraps in BASS_FX.
    private int OpenDeckHandle(string trackPath, out bool isStemDeck)
    {
        isStemDeck = false;
        StemSet? cached = _stemsEnabled ? _stemCache?.TryLoad(trackPath) : null;
        if (!StemDeckDecision.ShouldUseStems(_stemsEnabled, cached, out string reason))
        {
            _logger.LogDebug("Deck load uses single file ({Reason}): {Track}", reason, trackPath);
            return _backend.OpenDeckStream(trackPath);
        }

        try
        {
            _logger.LogInformation("Deck load uses 4-stem submix ({Model}): {Track}", cached!.ModelId, trackPath);
            int handle = _backend.OpenStemDeck(cached);
            isStemDeck = true;
            return handle;
        }
        catch (Exception ex)
        {
            // A corrupt/unopenable stem must never take down a deck — retry once with the single file.
            _logger.LogWarning(ex, "Stem deck open failed for {Track}; falling back to the single file.", trackPath);
            return _backend.OpenDeckStream(trackPath);
        }
    }

    // Caller holds _gate. Unplugs and forgets any deck in the slot, clearing its mixer channel and the
    // track-specific state (a new track gets fresh cues / BPM / loop). Transport state that belongs to
    // the DJ rather than the track (pitch, sync/quantize toggles) is intentionally left in place.
    private void UnloadSlot(int slot)
    {
        DeckSlot s = _slots[slot];
        if (s.Deck is not { } deck)
            return;
        _backend.ClearDeckLoop(deck.Handle); // drop any loop sync before the stream is freed
        _backend.UnplugDeck(deck.Handle);
        _mixer.SetChannel(slot, null);
        s.Deck = null;
        s.LoadedPath = null;
        s.TempCue = null;        // the temp cue belongs to the track — the new track starts with none
        Array.Clear(s.HotCues);
        s.BaseBpm = 0.0;         // base BPM belongs to the track — the new track supplies its own on load
        s.FirstBeat = 0.0;       // first-beat anchor likewise belongs to the track
        s.KickOnsets = Array.Empty<double>();
        s.Downbeat = 0.0;        // and so does the bar-1 anchor — stale, it would bar-snap the wrong "one"
        s.PhaseSyncReady = true; // grid confidence is per-track; default to confident/preserve until the load re-supplies it
        s.LoopBeats = 0.0;       // a new track has no active loop
        s.IsStemDeck = false;    // whether the NEXT track is a stem deck is decided at its load
        Array.Clear(s.StemMuted); // fresh decoders open at unity — stem mute is per-track, reset to audible
    }

    // End-of-track (A4): fired from the backend's end-of-stream sync (the BASS sync thread). Marks the
    // slot stopped under the gate, then raises DeckEnded OUTSIDE the lock so a subscriber that drives the
    // engine back (e.g. the live-queue binding loading the next track) does not run nested under _gate.
    // Guarded by handle so a stale callback from an already-replaced deck is ignored.
    private void OnDeckEnded(int slot, int handle)
    {
        lock (_gate)
        {
            if (_disposed || _slots[slot].Deck is not { } deck || deck.Handle != handle)
                return; // the slot was replaced/unloaded before the end fired — ignore the stale callback
            _slots[slot].Deck = deck with { Playing = false };
        }

        try
        {
            DeckEnded?.Invoke(this, slot);
        }
        catch (Exception ex)
        {
            // A misbehaving subscriber must not bubble onto the BASS sync thread (global #16/#26).
            _logger.LogError(ex, "A DeckEnded handler threw for deck slot {Slot}.", slot);
        }
    }
}
