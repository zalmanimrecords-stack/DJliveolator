using System.IO;

namespace Liveolator.Core.Library;

/// <summary>
/// A set of catalogued entries that appear to be the same file in two or more locations — the input
/// to the library's duplicate-cleanup UI. Always holds at least two entries with distinct paths.
/// </summary>
public sealed record DuplicateGroup<TEntry>(IReadOnlyList<TEntry> Entries) where TEntry : IMediaEntry;

/// <summary>
/// Finds duplicate library entries: files that share the same byte size AND the same (case-insensitive)
/// file name in different locations — the common "same track copied into two scan folders / re-downloaded"
/// case that bloats a library. Pure and storage-free (it reasons over already-scanned
/// <see cref="ScannedFile"/> metadata, never touching disk), so it unit-tests without hardware.
/// </summary>
/// <remarks>
/// The match is name + size, not content: it deliberately does NOT flag re-encodes (a different byte
/// size is treated as a different file). That keeps false positives near zero for the copied-file case;
/// a content-hash pass that also catches re-encodes is a future refinement.
/// </remarks>
public static class DuplicateFinder
{
    /// <summary>
    /// Groups entries that look like the same file (same size + file name) across different paths.
    /// Groups, and the entries within each group, are returned in a deterministic order (size, then
    /// name, then path) so callers and tests see stable output. Entries with a unique identity, and any
    /// accidental same-path repeat, are excluded.
    /// </summary>
    public static IReadOnlyList<DuplicateGroup<TEntry>> Find<TEntry>(IEnumerable<TEntry> entries)
        where TEntry : IMediaEntry
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(entry => entry is not null)
            .GroupBy(IdentityKey)
            .Where(group => group
                .Select(entry => entry.File.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= 2)
            .OrderBy(group => group.Key.SizeBytes)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
            .Select(group => new DuplicateGroup<TEntry>(
                group.OrderBy(entry => entry.File.Path, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    private static (long SizeBytes, string Name) IdentityKey<TEntry>(TEntry entry) where TEntry : IMediaEntry
        => (entry.File.SizeBytes, Path.GetFileName(entry.File.Path).ToLowerInvariant());
}
