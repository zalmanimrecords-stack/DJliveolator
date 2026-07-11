using Liveolator.Core.Audio.Sync;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Loop surface of <see cref="TwoDeckBassEngine"/>: beat-length loops sized from the deck's natural BPM
/// so the region stays musically <c>beats</c> beats regardless of pitch (doc 11). A loop needs a known
/// base BPM; without one the request is logged and dropped rather than guessing a wrong span.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public double LoopBeats(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].LoopBeats;
    }

    public bool IsLooping(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].LoopBeats > 0.0;
    }

    public void SetLoop(int slot, double beats)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck)
            {
                _logger.LogWarning("SetLoop deck slot {Slot} requested with no track loaded; ignoring.", slot);
                return;
            }
            if (s.BaseBpm <= 0.0)
            {
                // A beat-length loop needs the deck's tempo to size the region; without it, do nothing
                // rather than guess a wrong span (doc 11 loops are beat-synced to the deck grid).
                _logger.LogWarning("SetLoop deck slot {Slot} ignored: base BPM unknown.", slot);
                return;
            }
            if (beats < BeatLoopCalculator.MinBeats)
            {
                ClearLoopLocked(slot, deck);
                return;
            }

            // A loop already running is RESIZED live from its existing in-point (only the out-point moves) —
            // this is what turning the loop-length knob does while looping, and it matches halve/double's
            // pin-the-start behavior. Only a FRESH loop starts at the current playhead, grid-snapped when
            // Quantize is armed so the boundaries fall on the kick (doc 27 B3).
            double startSeconds;
            if (s.LoopBeats > 0.0)
            {
                startSeconds = s.LoopStartSeconds;
            }
            else
            {
                startSeconds = _backend.GetDeckPositionSeconds(deck.Handle);
                if (s.Quantize)
                    startSeconds = BeatLoopCalculator.SnapToBeat(startSeconds, s.FirstBeat, s.BaseBpm);
            }

            ApplyLoopLocked(slot, deck, startSeconds, beats);
        }
    }

    /// <summary>Halves the active loop length (down to the minimum), keeping the loop in-point fixed.</summary>
    public void HalveLoop(int slot) => ResizeLoop(slot, 0.5);

    /// <summary>Doubles the active loop length (up to the maximum), keeping the loop in-point fixed.</summary>
    public void DoubleLoop(int slot) => ResizeLoop(slot, 2.0);

    // Resize the active loop by a factor while pinning the in-point: halving brings the out-point in,
    // doubling pushes it out — the standard loop halve/double a DJ expects. A no-op when nothing loops.
    private void ResizeLoop(int slot, double factor)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck || s.LoopBeats <= 0.0)
                return; // nothing looping — nothing to resize

            double beats = Math.Clamp(
                s.LoopBeats * factor, BeatLoopCalculator.MinBeats, BeatLoopCalculator.MaxBeats);
            if (Math.Abs(beats - s.LoopBeats) < 1e-9)
                return; // already at the floor/ceiling — leave the region untouched

            ApplyLoopLocked(slot, deck, s.LoopStartSeconds, beats);
        }
    }

    // Caller holds _gate. Arms the backend loop region for <beats> beats from <startSeconds> and records
    // the in-point so a later halve/double resizes from the same start. Re-seats the playhead when a live
    // resize shrinks the region behind it, so the loop reshapes in real time instead of "escaping".
    private void ApplyLoopLocked(int slot, LoadedDeck deck, double startSeconds, double beats)
    {
        DeckSlot s = _slots[slot];
        LoopRegion region = BeatLoopCalculator.Region(startSeconds, beats, s.BaseBpm);

        // Near the track end a bar-loop can run past the file; clamp the out-point so the wrap sync still
        // fires (a sync armed beyond the stream length never triggers, and the loop would silently not hold).
        double trackLength = _backend.GetDeckLengthSeconds(deck.Handle);
        double endSeconds = region.EndSeconds;
        if (trackLength > 0.0 && endSeconds > trackLength)
            endSeconds = trackLength;
        if (endSeconds <= region.StartSeconds)
        {
            _logger.LogWarning("SetLoop deck slot {Slot} ignored: region collapses against the track end.", slot);
            return;
        }

        _backend.SetDeckLoop(deck.Handle, region.StartSeconds, endSeconds);
        s.LoopBeats = beats;
        s.LoopStartSeconds = region.StartSeconds;

        // A shrink (knob/halve while playing) can leave the playhead PAST the new out-point; the forward wrap
        // sync would then never fire and the loop would run off the end. Pull the playhead back into the
        // region, preserving beat phase, so resizing a running loop takes effect immediately (doc 11).
        double position = _backend.GetDeckPositionSeconds(deck.Handle);
        if (position >= endSeconds && trackLength > 0.0)
        {
            double wrapped = BeatLoopCalculator.WrapIntoRegion(position, region.StartSeconds, endSeconds - region.StartSeconds);
            _backend.SetDeckPositionFraction(deck.Handle, wrapped / trackLength);
        }

        _logger.LogInformation(
            "Deck slot {Slot} loop: {Beats} beats -> [{Start:F3}s, {End:F3}s).",
            slot, beats, region.StartSeconds, endSeconds);
    }

    public void ClearLoop(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_slots[slot].Deck is { } deck)
                ClearLoopLocked(slot, deck);
        }
    }

    // Caller holds _gate. Drop any active loop on the slot (backend + tracked beat length).
    private void ClearLoopLocked(int slot, LoadedDeck deck)
    {
        DeckSlot s = _slots[slot];
        if (s.LoopBeats <= 0.0)
            return;
        _backend.ClearDeckLoop(deck.Handle);
        s.LoopBeats = 0.0;
        _logger.LogInformation("Deck slot {Slot} loop cleared.", slot);
    }
}
