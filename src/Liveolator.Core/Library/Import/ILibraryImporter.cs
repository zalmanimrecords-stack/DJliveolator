using System.IO;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Parses one external DJ app's library file into the source-agnostic <see cref="LibraryImport"/> model.
/// The seam lives in Core (iron rule #3); concrete format parsers (Rekordbox XML, Traktor NML, …) live
/// in <c>Liveolator.Media</c>. Parsing reads a stream and does no Liveolator-side IO, so it is unit-
/// testable from an in-memory string and never touches the catalog/stores itself — mapping + persistence
/// are the planner's/service's job.
/// </summary>
public interface ILibraryImporter
{
    /// <summary>Human-readable source name for the UI/status (e.g. "Rekordbox", "Traktor").</summary>
    string FormatName { get; }

    /// <summary>
    /// Parse a library file's contents. Should be tolerant — a single malformed track/cue is skipped,
    /// not fatal — and never throw on recoverable data issues (global standards #16/#26). Throws only on
    /// a fundamentally unreadable file (e.g. not the expected format at all).
    /// </summary>
    LibraryImport Parse(Stream stream);
}
