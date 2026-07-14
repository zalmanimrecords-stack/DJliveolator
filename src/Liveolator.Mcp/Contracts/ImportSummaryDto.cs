using Liveolator.Core.Library.Import;

namespace Liveolator.Mcp.Contracts;

/// <summary>
/// Agent-facing result of importing another DJ app's library: how many tracks were added/updated, how
/// many source files could not be located on this machine, how many cue sets + playlists were imported,
/// and a one-line human summary. Mirrors <see cref="LibraryImportSummary"/> with the source format.
/// </summary>
public sealed record ImportSummaryDto(
    string Format,
    int TracksAdded,
    int TracksUpdated,
    int TracksUnresolved,
    int CuesImported,
    int CuesSkipped,
    int PlaylistsImported,
    int PlaylistTrackRefsDropped,
    string Message)
{
    public static ImportSummaryDto From(string format, LibraryImportSummary s) => new(
        format, s.TracksAdded, s.TracksUpdated, s.TracksUnresolved, s.CuesImported, s.CuesSkipped,
        s.PlaylistsImported, s.PlaylistTrackRefsDropped, s.Describe());
}
