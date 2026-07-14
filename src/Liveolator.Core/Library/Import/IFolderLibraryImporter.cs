namespace Liveolator.Core.Library.Import;

/// <summary>
/// Parses a folder-based DJ library (e.g. Serato, whose cues/beat-grids live in per-file tags and whose
/// crates are binary files under <c>_Serato_/Subcrates</c>) into the source-agnostic
/// <see cref="LibraryImport"/>. The companion to <see cref="ILibraryImporter"/> (which parses a single
/// exported file): a format whose data is spread across a directory tree implements this instead.
/// Concrete implementations live in <c>Liveolator.Media</c>; the result feeds the same mapping/merge
/// pipeline (<c>LibraryImportService</c>) as the file-based importers.
/// </summary>
public interface IFolderLibraryImporter
{
    /// <summary>Human-readable source name for the UI/status (e.g. "Serato").</summary>
    string FormatName { get; }

    /// <summary>
    /// Parse the library rooted at <paramref name="rootFolderPath"/>. Tolerant — an unreadable file/crate
    /// is skipped, never fatal (global standards #16/#26). Returns <see cref="LibraryImport.Empty"/> when
    /// the folder holds nothing recognizable.
    /// </summary>
    LibraryImport Parse(string rootFolderPath);
}
