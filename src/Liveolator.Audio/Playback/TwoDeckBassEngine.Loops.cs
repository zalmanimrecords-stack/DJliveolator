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

            // Convert the musical beat length to a concrete time region starting at the current playhead,
            // using the deck's natural BPM so the loop is musically <beats> beats regardless of pitch.
            double startSeconds = _backend.GetDeckPositionSeconds(deck.Handle);
            LoopRegion region = BeatLoopCalculator.Region(startSeconds, beats, s.BaseBpm);
            _backend.SetDeckLoop(deck.Handle, region.StartSeconds, region.EndSeconds);
            s.LoopBeats = beats;
            _logger.LogInformation(
                "Deck slot {Slot} loop: {Beats} beats -> [{Start:F3}s, {End:F3}s).",
                slot, beats, region.StartSeconds, region.EndSeconds);
        }
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
