namespace Liveolator.Core.Library.Import;

/// <summary>
/// What an import did, for the confirmation surfaced to the DJ (no silent success — global standard #26).
/// </summary>
/// <param name="TracksAdded">New tracks added to the catalog (path resolved, not previously catalogued).</param>
/// <param name="TracksUpdated">Existing tracks enriched with imported analysis.</param>
/// <param name="TracksUnresolved">Source tracks whose file could not be located on this machine (skipped).</param>
/// <param name="CuesImported">Tracks for which a hot-cue set was written.</param>
/// <param name="CuesSkipped">Tracks whose existing cues were kept (FillGaps) instead of overwritten.</param>
/// <param name="PlaylistsImported">Playlists saved.</param>
/// <param name="PlaylistTrackRefsDropped">Playlist entries dropped because their track did not resolve.</param>
public sealed record LibraryImportSummary(
    int TracksAdded,
    int TracksUpdated,
    int TracksUnresolved,
    int CuesImported,
    int CuesSkipped,
    int PlaylistsImported,
    int PlaylistTrackRefsDropped)
{
    public static LibraryImportSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>A one-line human summary for the status bar.</summary>
    public string Describe() =>
        $"Imported {TracksAdded} new + {TracksUpdated} updated track(s), {CuesImported} with cues, " +
        $"{PlaylistsImported} playlist(s)" +
        (TracksUnresolved > 0 ? $"; {TracksUnresolved} file(s) not found" : string.Empty) +
        (CuesSkipped > 0 ? $"; {CuesSkipped} kept existing cues" : string.Empty) + ".";
}
