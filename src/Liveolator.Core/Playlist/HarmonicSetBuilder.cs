using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist;

/// <summary>
/// Builds a harmonically-coherent set by chaining tracks: from the current track, pick the unused
/// candidate that is Camelot-compatible and fits the requested <see cref="BpmTrend"/>, preferring the
/// one the chain can run furthest from and then the smallest in-bounds jump. Pure and IO-free — it
/// operates over an in-memory candidate set so it unit-tests without hardware.
/// <para>The lookahead exists because the nearest candidate in tempo is often a harmonic cul-de-sac:
/// on a measured 63-track psytrance pool the pure-greedy pick stranded at 3 tracks where 19 were
/// reachable, and reported "no compatible track remains" for material that was there.</para>
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
            MusicTrack? next = PickNext(
                current, pool, options.BpmTolerance, options.Trend, options.Length - entries.Count);
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

    private static MusicTrack? PickNext(
        MusicTrack current,
        IReadOnlyList<MusicTrack> pool,
        double tolerance,
        BpmTrend trend,
        int budget)
    {
        MusicTrack? best = null;
        (int reach, double jump, int affinity, string title) bestScore = default;

        // Looking one pick ahead is what stops the chain stranding, but only a tempo-directional set is
        // cheap to look ahead in: Rising/Falling make the candidate graph acyclic, so the reachable depth
        // is exact and memoisable. Any/Steady can revisit a tempo, so they keep the pure greedy pick.
        MusicTrack[]? ordered = Monotone(trend) ? pool.ToArray() : null;
        Dictionary<(int, int), int>? memo = ordered is null ? null : new();

        foreach (MusicTrack candidate in pool)
        {
            if (!Camelot.IsCompatible(current.Key!.Camelot, candidate.Key!.Camelot))
                continue;
            if (!FitsTrend(current, candidate, tolerance, trend))
                continue;

            // Rank: how far the chain can still run from here, then the historical tie-breakers —
            // smallest tempo jump, closest harmonic affinity, title for determinism.
            int reach = ordered is null
                ? 0
                : ReachableDepth(Array.IndexOf(ordered, candidate), ordered, tolerance, trend, budget - 1, memo!);
            double jump = TempoJump(current, candidate);
            int affinity = HarmonicAffinity(current.Key!.Camelot, candidate.Key!.Camelot);
            var score = (reach, jump, affinity, candidate.Title);

            if (best is null || Less(score, bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool Monotone(BpmTrend trend)
        => trend is BpmTrend.Rising or BpmTrend.Falling;

    /// <summary>
    /// How many further tracks the chain can take from <paramref name="from"/>, within
    /// <paramref name="budget"/> steps. Moves are restricted to a strict (tempo, then pool index) order,
    /// which keeps the search acyclic — so the memo is sound and the number is a depth the chain really
    /// can reach, never an optimistic guess.
    /// </summary>
    private static int ReachableDepth(
        int from,
        MusicTrack[] pool,
        double tolerance,
        BpmTrend trend,
        int budget,
        Dictionary<(int, int), int> memo)
    {
        if (budget <= 0)
            return 0;
        if (memo.TryGetValue((from, budget), out int cached))
            return cached;

        int best = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            if (!Advances(pool, from, i))
                continue;
            if (!Camelot.IsCompatible(pool[from].Key!.Camelot, pool[i].Key!.Camelot))
                continue;
            if (!FitsTrend(pool[from], pool[i], tolerance, trend))
                continue;

            int depth = 1 + ReachableDepth(i, pool, tolerance, trend, budget - 1, memo);
            if (depth > best)
                best = depth;
        }

        memo[(from, budget)] = best;
        return best;
    }

    private static bool Advances(MusicTrack[] pool, int from, int to)
    {
        if (from == to)
            return false;
        double a = pool[from].Bpm?.Bpm ?? 0;
        double b = pool[to].Bpm?.Bpm ?? 0;
        return b > a || (Math.Abs(b - a) <= 1e-9 && to > from);
    }

    private static bool Less(
        (int reach, double jump, int affinity, string title) a,
        (int reach, double jump, int affinity, string title) b)
    {
        if (a.reach != b.reach)
            return a.reach > b.reach;
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
