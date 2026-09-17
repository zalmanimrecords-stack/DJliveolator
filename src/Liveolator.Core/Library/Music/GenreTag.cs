namespace Liveolator.Core.Library.Music;

/// <summary>
/// Turns a free-text genre tag into a comparable set of tokens.
/// <para>Genre tags are the dirtiest field in the catalog. Real values measured from a live library
/// include <c>"Melodic House &amp; Techno"</c>, <c>"Melodic House &amp; Techno | Melodic Techno"</c>,
/// <c>"Goa Trance/Psytrance"</c>, <c>"Goa, Psychedelic Trance, Electronic"</c> and
/// <c>"Techno ;Raw / Deep / Hypnotic; | Dub"</c> — four different separators, inconsistent spacing and
/// punctuation. Comparing those with string equality (which is what
/// <see cref="TrackQuery"/> still does) makes the first two different genres, which is how a
/// "psytrance" pool silently admitted techno.</para>
/// <para>So: split on every separator seen in real tags, strip everything that is not a letter or a
/// digit, and lower-case. <c>Psy-Trance</c>, <c>Psy Trance</c> and <c>Psytrance</c> all collapse to
/// one token, and two tags are related when their token sets OVERLAP rather than match whole.</para>
/// <para>Deliberately not a taxonomy: no stemming, no synonyms, no genre tree. <c>"Goa Trance"</c> and
/// <c>"Goa"</c> stay distinct because deciding they are the same is a musical judgement, not a string
/// operation. The caller's genre mode is the escape hatch for that.</para>
/// </summary>
public static class GenreTag
{
    private static readonly char[] Separators = { '|', ',', '/', ';' };

    /// <summary>
    /// The comparable tokens in <paramref name="genre"/>. Empty for null, blank, or punctuation-only
    /// input — an empty set means "unknown", never "matches nothing".
    /// </summary>
    public static IReadOnlySet<string> Normalize(string? genre)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(genre))
            return tokens;

        foreach (string part in genre.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = Canonicalize(part);
            if (token.Length > 0)
                tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// True when the two token sets share at least one genre. An empty set never intersects: an
    /// untagged track is unknown, and treating unknown as a match would let anything through.
    /// Callers decide what to do with unknown — this method only refuses to guess.
    /// </summary>
    public static bool Intersects(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return false;

        // Walk the smaller set; genre tag sets are tiny, but this keeps the cost obvious.
        IReadOnlySet<string> smaller = a.Count <= b.Count ? a : b;
        IReadOnlySet<string> larger = ReferenceEquals(smaller, a) ? b : a;

        foreach (string token in smaller)
        {
            if (larger.Contains(token))
                return true;
        }

        return false;
    }

    private static string Canonicalize(string part)
    {
        Span<char> buffer = part.Length <= 128 ? stackalloc char[part.Length] : new char[part.Length];
        int length = 0;

        foreach (char c in part)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }
}
