using Liveolator.Core.Enrichment;

namespace Liveolator.Online;

/// <summary>
/// Looks up a track's tempo/key by artist + title via the GetSongBPM web service (doc 16). The HTTP
/// call is behind this seam so the composing provider unit-tests with a fake.
/// </summary>
/// <remarks>
/// GetSongBPM is free <b>but requires a visible back-link to getsongbpm.com</b> wherever the data is
/// shown (their terms — accounts are suspended otherwise). The UI surfacing this data must include that
/// attribution.
/// </remarks>
public interface IGetSongBpmClient
{
    /// <summary>
    /// Searches for the track's tempo/key, or <c>null</c> when there is no match or the lookup cannot
    /// complete (offline-first — never throws for "not found"/transport errors).
    /// </summary>
    Task<OnlineTrackMetadata?> SearchAsync(string artist, string title, CancellationToken cancellationToken = default);
}
