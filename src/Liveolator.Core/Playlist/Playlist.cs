namespace Liveolator.Core.Playlist;

/// <summary>
/// A named, ordered set of track file paths the performer curated ahead of time (a "crate" / set).
/// Distinct from <see cref="ILivePlaylist"/> (the live Now/Next/Later queue): this is the saved,
/// reusable build artifact that can be loaded into the live queue. Pure data — paths only; titles
/// and analysis are looked up from the library at display time.
/// </summary>
public sealed record Playlist(string Name, IReadOnlyList<string> TrackPaths)
{
    /// <summary>An empty playlist with the given name.</summary>
    public static Playlist Empty(string name) => new(name, Array.Empty<string>());

    /// <summary>Returns a copy with the given ordered track paths (the name is unchanged).</summary>
    public Playlist WithTracks(IEnumerable<string> trackPaths)
        => this with { TrackPaths = trackPaths?.ToList() ?? throw new ArgumentNullException(nameof(trackPaths)) };
}
