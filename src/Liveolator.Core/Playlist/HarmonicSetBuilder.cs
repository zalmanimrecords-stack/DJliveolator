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

        // Genre can only judge a candidate when the SEED is tagged; an untagged seed opens the gate.
        IReadOnlySet<string> seedGenre = GenreTag.Normalize(
            options.Genre == GenreMatch.Strict ? seed.Metadata?.Genre : null);

        // Eligible pool: analyzed, keyed, in the seed's genre, and not the seed itself (compare by path,
        // the entry identity).
        var pool = candidates
            .Where(t => t.Status != MediaAnalysisStatus.Failed
                        && t.Key is not null
                        && !SamePath(t, seed)
                        && FitsGenre(seedGenre, t))
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

        // Looking ahead is what stops the chain stranding. Rising/Falling make the candidate graph
        // acyclic, so the reachable depth is exact and memoisable — that path is unchanged.
        MusicTrack[]? ordered = Monotone(trend) ? pool.ToArray() : null;
        Dictionary<(int, int), int>? memo = ordered is null ? null : new();

        // Any/Steady can revisit a tempo, so that exact depth is unsound for them and they used to fall
        // back to a pure greedy pick — which is what every in-app caller got, since Any is the default.
        // They get a bounded probe instead: deep enough to tell a dead end from a live branch, and run
        // only for the leading candidates, because probing a whole catalog on every pick is what makes
        // an unbounded cyclic search too slow to sit behind a button.
        IReadOnlyDictionary<string, int>? probed = ordered is null
            ? ProbeLeaders(current, pool, tolerance, trend, budget)
            : null;

        foreach (MusicTrack candidate in pool)
        {
            if (!Camelot.IsCompatible(current.Key!.Camelot, candidate.Key!.Camelot))
                continue;
            if (!FitsTrend(current, candidate, tolerance, trend))
                continue;

            // Rank: how far the chain can still run from here, then the historical tie-breakers —
            // smallest tempo jump, closest harmonic affinity, title for determinism.
            int reach = ordered is not null
                ? ReachableDepth(Array.IndexOf(ordered, candidate), ordered, tolerance, trend, budget - 1, memo!)
                : probed!.TryGetValue(candidate.File.Path, out int depth) ? depth : 0;
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

    /// <summary>
    /// Whether a candidate may join a set seeded in <paramref name="seedGenre"/>. Key and tempo alone
    /// let a 140 BPM techno track into a psytrance set, which is exactly how a measured "psytrance" pool
    /// came back mixed. Only a candidate we can positively place in ANOTHER genre is refused: an
    /// untagged track is unknown rather than wrong, and 73% of the measured catalog is untagged, so
    /// excluding those would starve every real set. An empty <paramref name="seedGenre"/> means the gate
    /// is off — either the seed carries no tag, or the caller asked for no gating.
    /// </summary>
    private static bool FitsGenre(IReadOnlySet<string> seedGenre, MusicTrack candidate)
    {
        if (seedGenre.Count == 0)
            return true;

        IReadOnlySet<string> tags = GenreTag.Normalize(candidate.Metadata?.Genre);
        return tags.Count == 0 || GenreTag.Intersects(seedGenre, tags);
    }

    private static bool Monotone(BpmTrend trend)
        => trend is BpmTrend.Rising or BpmTrend.Falling;

    /// <summary>How many picks past the candidate the cyclic probe looks.</summary>
    private const int ProbeDepth = 3;

    /// <summary>How many branches the probe follows at each step past the first.</summary>
    private const int ProbeBreadth = 4;

    /// <summary>How many of the leading candidates are probed at all; the rest score 0 and fall back
    /// to the greedy tie-breakers, which is exactly the behaviour they had before.</summary>
    private const int ProbedLeaders = 6;

    /// <summary>
    /// Bounded reachable depth for the leading candidates under a cyclic trend, keyed by track path.
    /// A candidate the probe never reached is simply absent — it scores 0, so the probe can only ever
    /// rescue the chain from a cul-de-sac, never demote a branch it did not look at.
    /// </summary>
    private static IReadOnlyDictionary<string, int> ProbeLeaders(
        MusicTrack current,
        IReadOnlyList<MusicTrack> pool,
        double tolerance,
        BpmTrend trend,
        int budget)
    {
        var probed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // On the last pick there is nothing left to strand into, so the probe has nothing to say.
        int depth = Math.Min(ProbeDepth, budget - 1);
        if (depth <= 0)
            return probed;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack leader in Leaders(current, pool, tolerance, trend, taken, ProbedLeaders))
        {
            taken.Add(leader.File.Path);
            probed[leader.File.Path] = BoundedReach(leader, pool, taken, tolerance, trend, depth);
            taken.Remove(leader.File.Path);
        }

        return probed;
    }

    /// <summary>
    /// How many further tracks the chain can take from <paramref name="from"/> within
    /// <paramref name="budget"/> steps, following at most <see cref="ProbeBreadth"/> branches per step.
    /// <paramref name="taken"/> carries the tracks already spent on this path, which is what keeps a
    /// cyclic graph from walking in circles — the depth is a chain the set really can run, not a count
    /// of edges.
    /// </summary>
    private static int BoundedReach(
        MusicTrack from,
        IReadOnlyList<MusicTrack> pool,
        HashSet<string> taken,
        double tolerance,
        BpmTrend trend,
        int budget)
    {
        if (budget <= 0)
            return 0;

        int best = 0;
        foreach (MusicTrack next in Leaders(from, pool, tolerance, trend, taken, ProbeBreadth))
        {
            taken.Add(next.File.Path);
            int depth = 1 + BoundedReach(next, pool, taken, tolerance, trend, budget - 1);
            taken.Remove(next.File.Path);

            if (depth > best)
                best = depth;
            if (best == budget)
                break; // a branch that runs the whole budget cannot be beaten within it.
        }

        return best;
    }

    /// <summary>
    /// The best next steps from <paramref name="from"/>, in the chain's own order — smallest tempo
    /// jump, closest harmonic affinity, then title. Ordering by the rule the chain itself picks by
    /// keeps the probe on the branches the set would really take, and keeps it deterministic.
    /// </summary>
    private static IEnumerable<MusicTrack> Leaders(
        MusicTrack from,
        IReadOnlyList<MusicTrack> pool,
        double tolerance,
        BpmTrend trend,
        HashSet<string> taken,
        int count)
        => pool
            .Where(t => !taken.Contains(t.File.Path)
                        && Camelot.IsCompatible(from.Key!.Camelot, t.Key!.Camelot)
                        && FitsTrend(from, t, tolerance, trend))
            .OrderBy(t => TempoJump(from, t))
            .ThenBy(t => HarmonicAffinity(from.Key!.Camelot, t.Key!.Camelot))
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .Take(count);

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
