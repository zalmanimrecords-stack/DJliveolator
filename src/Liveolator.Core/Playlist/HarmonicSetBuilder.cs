using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist;

/// <summary>
/// Builds a harmonically-coherent set by greedily chaining tracks: from the current track,
/// pick the unused candidate that is Camelot-compatible and whose tempo best fits the
/// requested <see cref="BpmTrend"/> with the smallest in-bounds jump. Pure and IO-free — it
/// operates over an in-memory candidate set so it unit-tests without hardware.
/// </summary>
public sealed class HarmonicSetBuilder
{
    /// <summary>
    /// Produces an ordered set starting at <paramref name="seed"/>, drawing from
    /// <paramref name="candidates"/>. Stops early when no compatible track remains; the result
    /// may therefore be shorter than the requested length, in which case
    /// <see cref="HarmonicSet.Unpicked"/> carries what was left and which rule kept it out.
    /// </summary>
    public HarmonicSet Build(MusicTrack seed, IEnumerable<MusicTrack> candidates, HarmonicSetOptions options)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (seed.Key is null)
            throw new ArgumentException("Seed track has no detected key; cannot build a harmonic set.", nameof(seed));

        // Eligible pool: analyzed, keyed, and not the seed itself (compare by path, the entry identity).
        var pool = candidates
            .Where(t => t.Status != MediaAnalysisStatus.Failed
                        && t.Key is not null
                        && !SamePath(t, seed))
            .ToList();

        var entries = new List<SetEntry> { new(seed, null) };
        MusicTrack current = seed;
        bool chainClosed = false;

        while (entries.Count < options.Length)
        {
            MusicTrack? next = PickNext(current, pool, options.BpmTolerance, options.Trend);
            if (next is null)
            {
                chainClosed = true;
                break;
            }

            entries.Add(new SetEntry(next, Rationalize(current, next)));
            pool.RemoveAll(t => SamePath(t, next));
            current = next;
        }

        return new HarmonicSet(entries, Unpicked(current, pool, chainClosed));
    }

    // The two predicates PickNext filters on carry no reason out of it, so the leftovers are re-tested
    // against the track the chain actually reached. Cheap (one pass over what is left) and it is the only
    // point where "why did this stop" is still knowable.
    private static IReadOnlyList<UnpickedCandidate> Unpicked(
        MusicTrack current,
        IReadOnlyList<MusicTrack> pool,
        bool chainClosed)
    {
        if (pool.Count == 0)
            return Array.Empty<UnpickedCandidate>();

        // Reaching the requested length is not a veto: nothing was asked of what is left, so naming a
        // reason for it would be a guess dressed up as a finding.
        if (!chainClosed)
            return pool.Select(t => new UnpickedCandidate(t, HarmonicVeto.NotTried)).ToArray();

        return pool
            .Select(t => new UnpickedCandidate(
                t,
                Camelot.IsCompatible(current.Key!.Camelot, t.Key!.Camelot)
                    ? HarmonicVeto.BlockedByTrend
                    : HarmonicVeto.NoCompatibleKey))
            .ToArray();
    }

    private static MusicTrack? PickNext(MusicTrack current, IReadOnlyList<MusicTrack> pool, double tolerance, BpmTrend trend)
    {
        MusicTrack? best = null;
        (double jump, int affinity, string title) bestScore = default;

        foreach (MusicTrack candidate in pool)
        {
            if (!Camelot.IsCompatible(current.Key!.Camelot, candidate.Key!.Camelot))
                continue;
            if (!FitsTrend(current, candidate, tolerance, trend))
                continue;

            // Rank: smallest tempo jump first, then closest harmonic affinity, then title for
            // deterministic tie-breaking (so the same inputs always yield the same set).
            double jump = TempoJump(current, candidate);
            int affinity = HarmonicAffinity(current.Key!.Camelot, candidate.Key!.Camelot);
            var score = (jump, affinity, candidate.Title);

            if (best is null || Less(score, bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool Less((double jump, int affinity, string title) a, (double jump, int affinity, string title) b)
    {
        if (Math.Abs(a.jump - b.jump) > 1e-9)
            return a.jump < b.jump;
        if (a.affinity != b.affinity)
            return a.affinity < b.affinity;
        return string.CompareOrdinal(a.title, b.title) < 0;
    }

    private static bool FitsTrend(MusicTrack current, MusicTrack candidate, double tolerance, BpmTrend trend)
    {
        // With no tempo on either side we can't reason about trend; allow only the unconstrained
        // mode so a missing-BPM track never silently violates a rising/falling/steady request.
        if (current.Bpm is null || candidate.Bpm is null)
            return trend == BpmTrend.Any;

        double delta = candidate.Bpm.Bpm - current.Bpm.Bpm;
        const double epsilon = 1e-6;
        return trend switch
        {
            BpmTrend.Any => true,
            BpmTrend.Steady => Math.Abs(delta) <= tolerance + epsilon,
            BpmTrend.Rising => delta >= -epsilon && delta <= tolerance + epsilon,
            BpmTrend.Falling => delta <= epsilon && delta >= -(tolerance + epsilon),
            _ => true
        };
    }

    private static double TempoJump(MusicTrack current, MusicTrack candidate)
        => current.Bpm is null || candidate.Bpm is null
            ? double.MaxValue / 2  // de-prioritize unknown-tempo tracks without excluding them
            : Math.Abs(candidate.Bpm.Bpm - current.Bpm.Bpm);

    /// <summary>0 = identical key, 1 = relative major/minor, 2 = adjacent ring — lower is closer.</summary>
    private static int HarmonicAffinity(string seed, string other)
    {
        if (string.Equals(seed, other, StringComparison.OrdinalIgnoreCase))
            return 0;
        // Same number, different letter → relative major/minor (closest non-identical move).
        if (seed.Length >= 2 && other.Length >= 2
            && seed.AsSpan(0, seed.Length - 1).SequenceEqual(other.AsSpan(0, other.Length - 1)))
            return 1;
        return 2;
    }

    private static TransitionRationale Rationalize(MusicTrack from, MusicTrack to)
    {
        string relationship = HarmonicAffinity(from.Key!.Camelot, to.Key!.Camelot) switch
        {
            0 => "same key",
            1 => "relative major/minor",
            _ => "adjacent key"
        };
        double? delta = from.Bpm is null || to.Bpm is null
            ? null
            : Math.Round(to.Bpm.Bpm - from.Bpm.Bpm, 2);
        return new TransitionRationale(relationship, delta);
    }

    private static bool SamePath(MusicTrack a, MusicTrack b)
        => string.Equals(a.File.Path, b.File.Path, StringComparison.OrdinalIgnoreCase);
}
