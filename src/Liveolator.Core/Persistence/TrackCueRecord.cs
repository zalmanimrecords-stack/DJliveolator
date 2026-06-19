using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Analysis.Cues;

namespace Liveolator.Core.Persistence;

/// <summary>
/// The on-disk shape of one track's cue set (doc 13). A plain serializable record decoupled from the
/// behaviour-rich <see cref="TrackCueSet"/>: the store maps between the two so the persisted contract
/// can stay stable even if the in-memory type evolves. Keyed by <see cref="TrackPath"/> in the store.
/// </summary>
/// <param name="TrackPath">Absolute file path of the track these cues belong to (the catalog key).</param>
/// <param name="SampleRate">Sample rate (Hz) the sample offsets were measured against.</param>
/// <param name="SlotCount">Number of hot-cue slots (mirrors the deck engine's hot-cue count).</param>
/// <param name="PrimaryCueSamples">Primary/temp cue position in samples, or null when unset.</param>
/// <param name="HotCues">The set hot cues; unset slots are omitted.</param>
public sealed record TrackCueRecord(
    string TrackPath,
    int SampleRate,
    int SlotCount,
    long? PrimaryCueSamples,
    IReadOnlyList<HotCue> HotCues)
{
    /// <summary>Projects a live <see cref="TrackCueSet"/> into its persistable record for a track path.</summary>
    public static TrackCueRecord FromCueSet(string trackPath, TrackCueSet set)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);
        ArgumentNullException.ThrowIfNull(set);
        return new TrackCueRecord(trackPath, set.SampleRate, set.SlotCount, set.PrimaryCueSamples, set.HotCues);
    }

    /// <summary>
    /// Rebuilds a live <see cref="TrackCueSet"/> from this record. Hot cues out of slot range are
    /// skipped (defensive against a hand-edited or older file) rather than throwing, so one bad cue
    /// never discards the whole track's cues (global standards #16/#26).
    /// </summary>
    public TrackCueSet ToCueSet()
    {
        var set = new TrackCueSet(SampleRate, SlotCount < 1 ? TrackCueSet.DefaultSlotCount : SlotCount);

        foreach (HotCue cue in HotCues ?? Enumerable.Empty<HotCue>())
        {
            if (cue.Index < 0 || cue.Index >= set.SlotCount || cue.PositionSamples < 0)
                continue;
            set = set.SetHotCue(cue.Index, cue.PositionSamples, cue.Label, cue.Color, cue.IsAuto);
        }

        if (PrimaryCueSamples is >= 0)
            set = set.SetPrimaryCue(PrimaryCueSamples.Value);

        return set;
    }
}
