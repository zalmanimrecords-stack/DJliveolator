using System.Collections.Generic;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// One playlist/crate parsed from another DJ app, as a name plus the ordered <em>source</em> track
/// paths it references. The planner remaps each path to the local catalog and drops references that
/// cannot be resolved (reporting the count) before persisting a Liveolator <c>Playlist</c>.
/// </summary>
/// <param name="Name">Display name. Nested source folders are flattened to "Folder / Sub / Name".</param>
/// <param name="SourceTrackPaths">Ordered source file paths the playlist references (pre-remap).</param>
public sealed record ImportedPlaylist(string Name, IReadOnlyList<string> SourceTrackPaths);
