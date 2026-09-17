using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist;

/// <summary>
/// Answers "what goes next after this track?" — genre and tempo FILTER, key only ORDERS what already
/// passed (owner decision, 2026-09-13). That split is deliberate and is the opposite of
/// <see cref="HarmonicSetBuilder"/>, where Camelot compatibility is a hard gate: a chained set must
/// stay mixable end to end, whereas a suggestion list must stay populated. The two answer different
/// questions and neither replaces the other.
/// <para>Pure and IO-free, like every rule in this namespace, and deterministic: the order is total,
/// tie-broken on title, so the same inputs always produce the same list.</para>
/// </summary>
public sealed class TrackSuggestionRule
{
    /// <summary>
    /// Ranks <paramref name="candidates"/> as follow-ons to <paramref name="seed"/>, best first.
    /// The seed itself and anything in <paramref name="exclude"/> (track paths, e.g. what the set
    /// already holds) never appear. Returns an empty list when nothing qualifies — a DJ with no
    /// compatible track needs to see that, not an exception.
    /// </summary>
    public IReadOnlyList<TrackSuggestion> Suggest(
        MusicTrack seed,
        IEnumerable<MusicTrack> candidates,
        TrackSuggestionOptions options,
        IEnumerable<string>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var skip = new HashSet<string>(
            exclude ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase) { seed.File.Path };

        // Genre can only judge a candidate when the SEED is tagged, and 73% of the measured catalog
        // is untagged — so an untagged seed opens the gate rather than emptying the list.
        IReadOnlySet<string> seedGenre =
            GenreTag.Normalize(options.Genre == GenreMatch.Off ? null : seed.Metadata?.Genre);
        bool judgeGenre = seedGenre.Count > 0;

        var ranked = new List<TrackSuggestion>();

        foreach (MusicTrack candidate in candidates)
        {
            if (candidate.Status == MediaAnalysisStatus.Failed)
                continue;
            if (skip.Contains(candidate.File.Path))
                continue;

            // Null delta = one side has no detected tempo, so there is nothing to gate on. The
            // candidate stays and sorts last rather than being excluded for missing data.
            double? bpmDelta = TempoDelta(seed, candidate);
            if (bpmDelta is not null && Math.Abs(bpmDelta.Value) > options.BpmTolerance)
                continue;

            int genreTier = 0;
            if (judgeGenre)
            {
                IReadOnlySet<string> tags = GenreTag.Normalize(candidate.Metadata?.Genre);
                genreTier = tags.Count == 0 ? 2 : GenreTag.Intersects(seedGenre, tags) ? 0 : 1;

                // Strict drops only what we can positively place in ANOTHER genre. An untagged track
                // is unknown, not wrong, so it survives demoted to the bottom tier.
                if (genreTier == 1 && options.Genre == GenreMatch.Strict)
                    continue;
            }

            ranked.Add(new TrackSuggestion(candidate, genreTier, bpmDelta, KeyTier(seed.Key, candidate.Key)));
        }

        return ranked
            .OrderBy(s => s.GenreTier)
            .ThenBy(s => s.BpmDelta is null ? double.MaxValue : Math.Abs(s.BpmDelta.Value))
            .ThenBy(s => s.KeyTier)
            .ThenBy(s => s.Track.Title, StringComparer.Ordinal)
            .Take(options.Limit)
            .ToList();
    }

    /// <summary>Signed tempo difference from seed to candidate; null when either side is unanalyzed.</summary>
    private static double? TempoDelta(MusicTrack seed, MusicTrack candidate)
        => seed.Bpm is null || candidate.Bpm is null ? null : candidate.Bpm.Bpm - seed.Bpm.Bpm;

    /// <summary>
    /// 0 = same Camelot code, 1 = relative major/minor, 2 = any other key, 3 = one side has no key.
    /// Derived from <see cref="Camelot.SortIndex"/> — which encodes a code as
    /// <c>number * 2 + (major ? 1 : 0)</c> — so the wheel is parsed in exactly one place.
    /// </summary>
    private static int KeyTier(MusicalKey? seed, MusicalKey? candidate)
    {
        int a = Camelot.SortIndex(seed?.Camelot);
        int b = Camelot.SortIndex(candidate?.Camelot);

        if (a == int.MaxValue || b == int.MaxValue)
            return 3;
        if (a == b)
            return 0;

        // Same wheel number with the letter switched: the relative major/minor of each other.
        return a / 2 == b / 2 ? 1 : 2;
    }
}
