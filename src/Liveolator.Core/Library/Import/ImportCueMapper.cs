using System;
using Liveolator.Core.Analysis.Cues;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Builds a <see cref="TrackCueSet"/> from a source track's cues. Source positions are in seconds; they
/// are converted to samples at a canonical rate (<see cref="SampleRate"/>) and the set stores that rate,
/// so recall stays exact regardless of the audio file's real sample rate (recall is sample÷rate = the
/// same time). Imported hot cues are committed manual cues (<c>IsAuto=false</c>) so re-analysis preserves
/// them. Rules: hot-cue indices 0..7 are placed (first cue wins a contested slot); an out-of-range index
/// is dropped; the first memory cue becomes the primary/temp cue.
/// </summary>
public static class ImportCueMapper
{
    /// <summary>Canonical sample rate used to store imported cue offsets (Hz).</summary>
    public const int SampleRate = 44_100;

    /// <summary>
    /// Maps the track's cues into a fresh 8-slot cue set. Returns an empty set when there is nothing to
    /// place. <paramref name="droppedCues"/> reports cues discarded (slot collision or out-of-range).
    /// </summary>
    public static TrackCueSet Map(ImportedTrack track, out int droppedCues)
    {
        droppedCues = 0;
        var set = new TrackCueSet(SampleRate, TrackCueSet.DefaultSlotCount);
        if (track.Cues is null)
            return set;

        bool primaryPlaced = false;
        foreach (ImportedCue cue in track.Cues)
        {
            long samples = (long)Math.Round(Math.Max(0.0, cue.PositionSeconds) * SampleRate);

            if (cue.IsMemoryCue)
            {
                if (primaryPlaced)
                {
                    droppedCues++; // keep only the first memory cue as the primary
                    continue;
                }
                set = set.SetPrimaryCue(samples);
                primaryPlaced = true;
                continue;
            }

            if (cue.Index < 0 || cue.Index >= set.SlotCount || set.IsHotCueSet(cue.Index))
            {
                droppedCues++; // out of range, or a cue already claimed this slot (first wins)
                continue;
            }

            set = set.SetHotCue(cue.Index, samples, cue.Label, cue.Color, isAuto: false);
        }

        return set;
    }
}
