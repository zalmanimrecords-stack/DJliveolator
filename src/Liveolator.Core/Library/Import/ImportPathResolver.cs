using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Maps a source app's track path to a real file on this machine — the single biggest real-world import
/// problem, because the source library was usually built on another drive/OS (e.g. a macOS
/// <c>/Users/…</c> path, or this rig's network drive). Strategy, in order:
/// <list type="number">
///   <item>The literal source path, if it exists (probed via the injected <c>stat</c>).</item>
///   <item>A by-filename match against the already-catalogued tracks; if several share the filename,
///   only a duration match (within a small tolerance) is accepted — otherwise the track is left
///   unresolved rather than risk putting cues on the wrong file.</item>
/// </list>
/// Returns the resolved <see cref="ScannedFile"/> (reusing the catalog entry's real size/mtime on a
/// filename match) or null when the file cannot be located.
/// </summary>
public sealed class ImportPathResolver
{
    private const double DurationToleranceSeconds = 2.0;

    private readonly Func<string, ScannedFile?> _stat;
    private readonly IReadOnlyDictionary<string, List<MusicTrack>> _byFileName;

    /// <param name="catalog">The current catalog, used for by-filename remapping.</param>
    /// <param name="stat">Probes a path: returns its <see cref="ScannedFile"/> if it exists, else null.</param>
    public ImportPathResolver(IReadOnlyCollection<MusicTrack> catalog, Func<string, ScannedFile?> stat)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _stat = stat ?? throw new ArgumentNullException(nameof(stat));
        _byFileName = catalog
            .GroupBy(t => Path.GetFileName(t.File.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Resolve a source path to a local file, or null when it cannot be located.</summary>
    public ScannedFile? Resolve(string? sourcePath, double? durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        if (_stat(sourcePath) is { } literal)
            return literal;

        string fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(fileName) || !_byFileName.TryGetValue(fileName, out List<MusicTrack>? matches))
            return null;

        if (matches.Count == 1)
            return matches[0].File;

        // Several files share the name — only commit to one if a duration confirms it; never guess, since a
        // wrong match would scatter cues onto the wrong track (global standard #26 — no silent mis-handling).
        if (durationSeconds is > 0)
        {
            MusicTrack? best = matches
                .Where(m => m.Duration is not null)
                .OrderBy(m => Math.Abs(m.Duration!.Value.TotalSeconds - durationSeconds.Value))
                .FirstOrDefault();
            if (best is not null &&
                Math.Abs(best.Duration!.Value.TotalSeconds - durationSeconds.Value) <= DurationToleranceSeconds)
                return best.File;
        }

        return null;
    }
}
