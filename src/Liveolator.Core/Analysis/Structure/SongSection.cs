namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// One section boundary of a track's musical structure (intro / build-up / drop / breakdown /
/// outro / generic section), as detected by the offline structure analyzer (doc 32). A section
/// is identified only by where it <em>starts</em>; its end is the next section's start (or the
/// track end). Pure data.
/// </summary>
/// <param name="StartSeconds">Section start in seconds from track start (non-negative).</param>
/// <param name="Label">Musical role label — see <see cref="SongSectionLabel"/> for the known values.</param>
public sealed record SongSection(double StartSeconds, string Label);
