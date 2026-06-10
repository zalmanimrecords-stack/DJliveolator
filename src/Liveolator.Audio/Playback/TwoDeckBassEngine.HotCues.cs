using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Audio;
using Liveolator.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Hot-cue surface of <see cref="TwoDeckBassEngine"/>: the per-deck pad bank (set on first press, jump
/// on later presses) and its tolerant persistence (A3) — a store hiccup degrades to no-cues, never a
/// failed load or a crashed show.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public int HotCueCount => HotCuesPerDeck;

    public bool IsHotCueSet(int slot, int cueIndex)
    {
        ValidateSlot(slot);
        if (cueIndex < 0 || cueIndex >= HotCuesPerDeck)
            return false;
        lock (_gate) return _slots[slot].HotCues[cueIndex].HasValue;
    }

    public void HotCue(int slot, int cueIndex)
    {
        ValidateSlot(slot);
        if (cueIndex < 0 || cueIndex >= HotCuesPerDeck)
            throw new ArgumentOutOfRangeException(nameof(cueIndex), cueIndex, "Hot-cue index is out of range.");
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck)
                return; // nothing loaded — no position to store or jump to
            if (s.HotCues[cueIndex] is { } position)
            {
                _backend.SetDeckPositionFraction(deck.Handle, position); // jump to the stored cue
            }
            else
            {
                s.HotCues[cueIndex] = _backend.GetDeckPositionFraction(deck.Handle); // set at current position
                SavePersistedHotCues(slot, deck.Handle); // a newly set cue survives the next load/restart
            }
        }
    }

    // Caller holds _gate. Load a track's persisted cue set (A3) and project the sample-based cues onto
    // this deck's 0..1 fraction bank using the deck length. No store, no length, or an unreadable file
    // all leave the (already-cleared) bank empty — a persistence hiccup must never crash a load.
    private void LoadPersistedHotCues(int slot, int handle, string trackPath)
    {
        if (_hotCueStore is null)
            return;

        try
        {
            TrackCueRecord? record = _hotCueStore.LoadAsync(trackPath).GetAwaiter().GetResult();
            if (record is null)
                return;

            DeckSlot s = _slots[slot];
            double lengthSeconds = _backend.GetDeckLengthSeconds(handle);
            int sampleRate = record.SampleRate > 0 ? record.SampleRate : _sampleRate;
            foreach (HotCue cue in record.HotCues)
            {
                if (cue.Index < 0 || cue.Index >= HotCuesPerDeck)
                    continue; // tolerate a hand-edited / wider-bank file
                s.HotCues[cue.Index] =
                    HotCuePositionMapper.SamplesToFraction(cue.PositionSamples, lengthSeconds, sampleRate);
            }
            _logger.LogInformation(
                "Deck slot {Slot}: restored {Count} persisted hot cue(s) for {Track}.",
                slot, record.HotCues.Count, trackPath);
        }
        catch (Exception ex)
        {
            // Degrade to no-cues rather than failing the load (global standards #16/#26).
            _logger.LogWarning(ex, "Could not load persisted hot cues for deck slot {Slot} <- {Track}.", slot, trackPath);
        }
    }

    // Caller holds _gate. Persist the slot's current cue bank (A3), keyed by the loaded path, as a
    // sample-based record. Fire-and-forget so a pad press stays instant; a failed save is logged, never
    // thrown, and never blocks the show. Reads the bank snapshot now (under the gate) so the async write
    // does not race a later cue edit.
    private void SavePersistedHotCues(int slot, int handle)
    {
        DeckSlot s = _slots[slot];
        if (_hotCueStore is null || s.LoadedPath is not { } trackPath)
            return;

        double lengthSeconds = _backend.GetDeckLengthSeconds(handle);
        var set = new TrackCueSet(_sampleRate > 0 ? _sampleRate : 1, HotCuesPerDeck);
        for (int i = 0; i < HotCuesPerDeck; i++)
        {
            if (s.HotCues[i] is { } fraction)
                set = set.SetHotCue(i, HotCuePositionMapper.FractionToSamples(fraction, lengthSeconds, _sampleRate));
        }

        TrackCueRecord record = TrackCueRecord.FromCueSet(trackPath, set);
        try
        {
            // Fire-and-forget: a pad press stays instant. Both a synchronous throw (a misbehaving store)
            // and an async fault are logged and dropped, never crashing the show (global #16/#26).
            _ = _hotCueStore.SaveAsync(record).ContinueWith(
                task => _logger.LogWarning(
                    task.Exception?.GetBaseException(),
                    "Could not persist hot cues for deck slot {Slot} <- {Track}.", slot, trackPath),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist hot cues for deck slot {Slot} <- {Track}.", slot, trackPath);
        }
    }
}
