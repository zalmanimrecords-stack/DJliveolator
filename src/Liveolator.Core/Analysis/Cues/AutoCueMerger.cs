using System;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Merges freshly-computed auto cues into a track's existing stored cues so re-analysis never clobbers
/// the DJ's work (owner decision 2026-06-19: "suggested → commit"). The rule, applied per slot:
/// <list type="bullet">
///   <item>A <em>manual</em> existing cue (<see cref="HotCue.IsAuto"/> = false — one the DJ set, moved or
///   committed by pressing it) is preserved verbatim; the new analysis is ignored for that slot.</item>
///   <item>An <em>auto</em> or <em>empty</em> slot adopts the new auto cue (or stays empty when the new
///   analysis no longer finds anything there — stale auto cues are cleared, not left behind).</item>
/// </list>
/// The primary/temp cue is always preserved from the existing set (it is a manual choice). Pure and
/// deterministic.
/// </summary>
public sealed class AutoCueMerger
{
    /// <summary>
    /// Returns a new cue set: <paramref name="existing"/> with its auto/empty slots replaced by
    /// <paramref name="auto"/>'s cues, keeping every manual cue and the existing primary cue. The result
    /// adopts <paramref name="auto"/>'s sample rate and slot count (the current analysis grid); preserved
    /// manual cue positions are rescaled when the two sets were measured at different sample rates so they
    /// stay at the same point in time.
    /// </summary>
    /// <param name="existing">The stored cue set (may be null — treated as "no stored cues").</param>
    /// <param name="auto">The newly-computed auto cue set (every cue is <see cref="HotCue.IsAuto"/>).</param>
    public TrackCueSet Merge(TrackCueSet? existing, TrackCueSet auto)
    {
        ArgumentNullException.ThrowIfNull(auto);

        if (existing is null)
            return auto;

        var result = new TrackCueSet(auto.SampleRate, auto.SlotCount);
        double rescale = (double)auto.SampleRate / existing.SampleRate;

        for (int slot = 0; slot < result.SlotCount; slot++)
        {
            HotCue? manual = slot < existing.SlotCount ? existing.GetHotCue(slot) : null;
            if (manual is { IsAuto: false } kept)
            {
                long rescaled = (long)Math.Round(kept.PositionSamples * rescale);
                result = result.SetHotCue(slot, rescaled < 0 ? 0 : rescaled, kept.Label, kept.Color, isAuto: false);
                continue;
            }

            if (auto.GetHotCue(slot) is { } fresh)
                result = result.SetHotCue(slot, fresh.PositionSamples, fresh.Label, fresh.Color, fresh.IsAuto);
        }

        // The primary/temp cue is the DJ's manual choice — carry it over, rescaled to the new grid.
        if (existing.PrimaryCueSamples is { } primary)
            result = result.SetPrimaryCue((long)Math.Round(primary * rescale));

        return result;
    }
}
