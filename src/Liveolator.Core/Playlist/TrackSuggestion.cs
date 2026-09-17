using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist;

/// <summary>
/// One suggested next track, carrying the reason it was ranked where it was. The tiers are exposed
/// rather than collapsed into a score because the UI has to be able to say WHY a row is there — a
/// suggestion a DJ cannot explain is one they will not trust.
/// </summary>
/// <param name="Track">The suggested track.</param>
/// <param name="GenreTier">0 = genre overlaps the seed, 1 = known but different, 2 = unknown.
/// Always 0 when genre could not be applied (seed untagged, or <see cref="GenreMatch.Off"/>).</param>
/// <param name="BpmDelta">Signed tempo difference from the seed, or null when either side has no
/// detected tempo. Null sorts last without being excluded.</param>
/// <param name="KeyTier">0 = same Camelot code, 1 = relative major/minor, 2 = any other key,
/// 3 = one side has no key. Orders only; it can never rescue a track the filters rejected.</param>
public sealed record TrackSuggestion(
    MusicTrack Track,
    int GenreTier,
    double? BpmDelta,
    int KeyTier);
