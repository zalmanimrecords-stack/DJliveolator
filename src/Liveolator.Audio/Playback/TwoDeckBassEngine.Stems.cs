using Liveolator.Core.Analysis.Stems;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Stem surface of <see cref="TwoDeckBassEngine"/> (doc 32 §Phase 2b): per-deck, per-stem mute for a deck
/// loaded as a 4-stem submix. Mute is a per-track transition gesture — it is reset to all-audible on every
/// load (<c>UnloadSlot</c> clears <see cref="DeckSlot.StemMuted"/>), unlike the persistent pitch/EQ. A
/// single-file deck has no stems, so every method here is a safe no-op for it.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public bool IsStemDeck(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            return s.Deck is not null && s.IsStemDeck;
        }
    }

    public bool IsStemMuted(int slot, StemKind kind)
    {
        ValidateSlot(slot);
        int index = StemSet.IndexOf(kind);
        if (index < 0)
            return false;
        lock (_gate) return _slots[slot].StemMuted[index];
    }

    public void SetStemMuted(int slot, StemKind kind, bool muted)
    {
        ValidateSlot(slot);
        int index = StemSet.IndexOf(kind);
        if (index < 0)
            return;
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            // Nothing loaded, or a plain single-file deck: mute has no meaning, so don't record state (that
            // would leave IsStemMuted reporting a muted stem on a deck that has none) or touch the backend.
            if (s.Deck is not { } deck || !s.IsStemDeck)
                return;
            s.StemMuted[index] = muted;
            _backend.SetStemEnabled(deck.Handle, kind, enabled: !muted);
        }
    }
}
