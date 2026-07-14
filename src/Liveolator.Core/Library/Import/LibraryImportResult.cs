using System.Collections.Generic;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// What a <see cref="LibraryImportService"/> run produced. Cues and playlists are already persisted by
/// the service; <see cref="TracksToUpsert"/> are the new/enriched catalog entries the caller merges into
/// the in-memory <c>MusicLibrary</c> and persists (the catalog write stays the caller's concern, exactly
/// as a scan does).
/// </summary>
/// <param name="TracksToUpsert">New + changed tracks to merge into the catalog (by path; import wins).</param>
/// <param name="Summary">Human-facing counts of what happened.</param>
public sealed record LibraryImportResult(
    IReadOnlyList<MusicTrack> TracksToUpsert,
    LibraryImportSummary Summary);
