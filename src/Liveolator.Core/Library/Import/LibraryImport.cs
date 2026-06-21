using System;
using System.Collections.Generic;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// The source-agnostic result of parsing another DJ app's library file: its tracks (with analysis +
/// cues) and its playlists. Every concrete importer (<see cref="ILibraryImporter"/>) produces this one
/// shape, so the planner/service that maps it into Liveolator is written once, format-independent.
/// </summary>
/// <param name="Tracks">The parsed tracks.</param>
/// <param name="Playlists">The parsed playlists/crates (may be empty).</param>
public sealed record LibraryImport(
    IReadOnlyList<ImportedTrack> Tracks,
    IReadOnlyList<ImportedPlaylist> Playlists)
{
    /// <summary>An empty import (no tracks, no playlists).</summary>
    public static LibraryImport Empty { get; } =
        new(Array.Empty<ImportedTrack>(), Array.Empty<ImportedPlaylist>());
}
