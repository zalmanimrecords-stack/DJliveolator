namespace Liveolator.Core.Enrichment;

/// <summary>
/// What we know about a track when asking an <see cref="IMetadataProvider"/> to look it up online
/// (doc 16). Identification prefers an acoustic fingerprint (<see cref="AcoustId"/> + duration) over
/// tags, since filenames/tags are unreliable; artist/title are the fallback match. Pure data.
/// </summary>
/// <param name="Artist">Track artist from tags, or null.</param>
/// <param name="Title">Track title from tags / filename, or null.</param>
/// <param name="AcoustId">Chromaprint/AcoustID fingerprint id, when computed; null otherwise.</param>
/// <param name="Duration">Track duration (helps disambiguate fingerprint matches); null if unknown.</param>
public sealed record TrackQuery(
    string? Artist = null,
    string? Title = null,
    string? AcoustId = null,
    TimeSpan? Duration = null)
{
    /// <summary>True when artist + title are both present (the minimum for a tag-based lookup).</summary>
    public bool HasTags => !string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title);

    /// <summary>True when an acoustic fingerprint is available (the preferred, filename-independent match).</summary>
    public bool HasFingerprint => !string.IsNullOrWhiteSpace(AcoustId);
}
