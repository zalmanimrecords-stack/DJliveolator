using System.Collections.Generic;
using System.Linq;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// The persistent set of cue points for one track (doc 11/16): a fixed bank of indexed
/// <see cref="HotCue"/> slots plus a single primary/temp cue (the position the deck's
/// <c>Cue</c> button jumps to). Immutable — every mutation returns a new set — so it is safe to
/// share across the load → engine → save round-trip and trivially unit-testable.
/// </summary>
/// <remarks>
/// Positions are stored in samples and paired with <see cref="SampleRate"/>; this keeps recall
/// sample-accurate and lets callers convert to seconds or to the engine's normalized 0..1
/// position (sample ÷ total samples) without losing precision. <see cref="SlotCount"/> mirrors
/// the deck engine's <c>HotCueCount</c> so a set maps 1:1 onto the engine's hot-cue bank.
/// </remarks>
public sealed class TrackCueSet
{
    private readonly HotCue?[] _slots;

    /// <summary>Default number of hot-cue slots, matching the CMD STUDIO 2A's 8 pads (doc 11).</summary>
    public const int DefaultSlotCount = 8;

    /// <summary>Sample rate (Hz) the cue sample offsets are measured against.</summary>
    public int SampleRate { get; }

    /// <summary>Number of hot-cue slots in this set (valid indices are 0..SlotCount-1).</summary>
    public int SlotCount => _slots.Length;

    /// <summary>
    /// The primary/temp cue position in samples (the deck's <c>Cue</c> target), or null when none is
    /// set — in which case <c>Cue</c> falls back to track start (sample 0), per the engine contract.
    /// </summary>
    public long? PrimaryCueSamples { get; }

    public TrackCueSet(int sampleRate, int slotCount = DefaultSlotCount)
        : this(sampleRate, slotCount, primaryCueSamples: null, slots: null)
    {
    }

    private TrackCueSet(int sampleRate, int slotCount, long? primaryCueSamples, HotCue?[]? slots)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "Slot count must be at least 1.");
        if (primaryCueSamples is < 0)
            throw new ArgumentOutOfRangeException(nameof(primaryCueSamples), primaryCueSamples, "Cue position cannot be negative.");

        SampleRate = sampleRate;
        PrimaryCueSamples = primaryCueSamples;
        _slots = slots ?? new HotCue?[slotCount];
    }

    /// <summary>All set hot cues, ordered by slot index (unset slots are omitted).</summary>
    public IReadOnlyList<HotCue> HotCues =>
        _slots.Where(c => c is not null).Select(c => c!.Value).ToList();

    /// <summary>Returns the hot cue at <paramref name="index"/>, or null when the slot is empty.</summary>
    public HotCue? GetHotCue(int index)
    {
        ValidateIndex(index);
        return _slots[index];
    }

    /// <summary>True when the slot at <paramref name="index"/> holds a hot cue.</summary>
    public bool IsHotCueSet(int index)
    {
        ValidateIndex(index);
        return _slots[index] is not null;
    }

    /// <summary>
    /// Returns a new set with the hot cue at <paramref name="index"/> placed at
    /// <paramref name="positionSamples"/>, overwriting any existing cue in that slot. The cue's
    /// <see cref="HotCue.Index"/> is forced to <paramref name="index"/> so the slot and cue agree.
    /// </summary>
    public TrackCueSet SetHotCue(int index, long positionSamples, string? label = null, int? color = null)
    {
        ValidateIndex(index);
        if (positionSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(positionSamples), positionSamples, "Cue position cannot be negative.");

        HotCue?[] next = (HotCue?[])_slots.Clone();
        next[index] = new HotCue(index, positionSamples, label, color);
        return new TrackCueSet(SampleRate, SlotCount, PrimaryCueSamples, next);
    }

    /// <summary>
    /// Returns a new set with the hot cue at <paramref name="index"/> removed. Clearing an already
    /// empty slot is a no-op that returns an equivalent set (idempotent).
    /// </summary>
    public TrackCueSet ClearHotCue(int index)
    {
        ValidateIndex(index);
        if (_slots[index] is null)
            return this;

        HotCue?[] next = (HotCue?[])_slots.Clone();
        next[index] = null;
        return new TrackCueSet(SampleRate, SlotCount, PrimaryCueSamples, next);
    }

    /// <summary>
    /// Recalls the hot cue at <paramref name="index"/>: returns its position in samples, or null when
    /// the slot is empty. This is the pure position math the deck uses to jump the playhead.
    /// </summary>
    public long? RecallSamples(int index)
    {
        ValidateIndex(index);
        return _slots[index]?.PositionSamples;
    }

    /// <summary>
    /// Recalls a hot cue quantized to the nearest beat boundary. Given the track's tempo in beats
    /// per minute, the stored sample position is snapped to the closest beat grid line (anchored at
    /// sample 0). Returns null when the slot is empty. Use this for the engine's quantize-enabled
    /// recall so cue jumps land on the beat (doc 11 quantize toggle).
    /// </summary>
    public long? RecallQuantizedSamples(int index, double beatsPerMinute)
    {
        ValidateIndex(index);
        if (beatsPerMinute <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerMinute), beatsPerMinute, "BPM must be positive.");

        if (_slots[index] is not { } cue)
            return null;

        double samplesPerBeat = SampleRate * 60.0 / beatsPerMinute;
        long beatIndex = (long)System.Math.Round(cue.PositionSamples / samplesPerBeat);
        return (long)System.Math.Round(beatIndex * samplesPerBeat);
    }

    /// <summary>Returns a new set whose primary/temp cue is placed at <paramref name="positionSamples"/>.</summary>
    public TrackCueSet SetPrimaryCue(long positionSamples)
    {
        if (positionSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(positionSamples), positionSamples, "Cue position cannot be negative.");
        return new TrackCueSet(SampleRate, SlotCount, positionSamples, (HotCue?[])_slots.Clone());
    }

    /// <summary>Returns a new set with the primary/temp cue cleared (so <c>Cue</c> falls back to start).</summary>
    public TrackCueSet ClearPrimaryCue()
        => new(SampleRate, SlotCount, primaryCueSamples: null, (HotCue?[])_slots.Clone());

    /// <summary>
    /// The primary/temp cue target in samples — the stored primary cue, or sample 0 (track start)
    /// when none is set. This mirrors the engine's <c>Cue</c> contract (jump to cue, else start).
    /// </summary>
    public long PrimaryCueTargetSamples => PrimaryCueSamples ?? 0L;

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= _slots.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Hot-cue index must be in 0..{_slots.Length - 1}.");
    }
}
