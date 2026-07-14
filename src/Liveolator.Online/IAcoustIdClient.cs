namespace Liveolator.Online;

/// <summary>A recording matched by acoustic fingerprint (the best-scoring AcoustID result).</summary>
/// <param name="Artist">Joined artist name(s).</param>
/// <param name="Title">Recording title.</param>
/// <param name="Score">AcoustID match score (0..1).</param>
public sealed record RecordingMatch(string Artist, string Title, double Score);

/// <summary>
/// Identifies a track from its Chromaprint fingerprint via the AcoustID web service (doc 16) →
/// MusicBrainz recording (artist + title). Filename-independent, the preferred match. The HTTP call is
/// behind this seam so the composing provider unit-tests with a fake.
/// </summary>
public interface IAcoustIdClient
{
    /// <summary>
    /// Looks up the best fingerprint match, or <c>null</c> when there is no confident match or the
    /// lookup cannot complete (offline-first — never throws for "not found"/transport errors).
    /// </summary>
    Task<RecordingMatch?> LookupAsync(string fingerprint, int durationSeconds, CancellationToken cancellationToken = default);
}
