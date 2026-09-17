namespace Liveolator.Core.Playlist;

/// <summary>How hard the genre signal is applied when suggesting the next track.</summary>
public enum GenreMatch
{
    /// <summary>A candidate whose genre is known and does not overlap the seed's is excluded.</summary>
    Strict,

    /// <summary>Nothing is excluded on genre; matches simply rank above mismatches.</summary>
    Loose,

    /// <summary>Genre is ignored for both filtering and ordering — tempo and key alone decide.</summary>
    Off
}

/// <summary>
/// Shape of a "what goes next" request. Genre and tempo FILTER; key only orders what already passed
/// (owner decision, 2026-09-13), which is the opposite of <see cref="HarmonicSetOptions"/> — there,
/// key is a hard gate. The two rules answer different questions and neither replaces the other.
/// </summary>
/// <param name="Limit">Maximum suggestions returned.</param>
/// <param name="BpmTolerance">Largest tempo difference from the seed, in BPM. Defaults to the same
/// 6.0 the harmonic set builder uses, so the two surfaces agree on what "close" means.</param>
/// <param name="Genre">How strictly the genre signal is applied. <see cref="GenreMatch.Off"/> is the
/// escape hatch for a library where most tracks are untagged.</param>
public sealed record TrackSuggestionOptions(
    int Limit = 20,
    double BpmTolerance = 6.0,
    GenreMatch Genre = GenreMatch.Strict)
{
    /// <summary>Validates the request, throwing for values that cannot produce suggestions.</summary>
    public void Validate()
    {
        if (Limit < 1)
            throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "Suggestion limit must be at least 1.");
        if (BpmTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(BpmTolerance), BpmTolerance, "BPM tolerance cannot be negative.");
    }
}
