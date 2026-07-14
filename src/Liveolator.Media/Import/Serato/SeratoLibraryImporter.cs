using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import.Serato;

/// <summary>
/// Imports a Serato library from a folder: reads each ID3-tagged audio file's "Serato Markers2" (hot
/// cues) and "Serato BeatGrid" (BPM + anchor) GEOB frames, and reads the <c>_Serato_/Subcrates/*.crate</c>
/// files as playlists. Unlike Rekordbox/Traktor there is no single export file — the data is spread across
/// the audio files and the <c>_Serato_</c> folder — so this is an <see cref="IFolderLibraryImporter"/>.
/// Phase scope: ID3 containers (MP3/AIFF); FLAC/MP4 store Serato data differently and are not yet read.
/// Tolerant: an unreadable file or crate is skipped, never fatal (global standards #16/#26).
/// </summary>
public sealed class SeratoLibraryImporter : IFolderLibraryImporter
{
    private static readonly string[] Id3AudioExtensions = { ".mp3", ".aif", ".aiff" };
    private const string Markers2 = "Serato Markers2";
    private const string BeatGrid = "Serato BeatGrid";

    public string FormatName => "Serato";

    public LibraryImport Parse(string rootFolderPath)
    {
        if (string.IsNullOrWhiteSpace(rootFolderPath) || !Directory.Exists(rootFolderPath))
            return LibraryImport.Empty;

        return new LibraryImport(ReadTrackCues(rootFolderPath), ReadCrates(rootFolderPath));
    }

    private static IReadOnlyList<ImportedTrack> ReadTrackCues(string root)
    {
        var tracks = new List<ImportedTrack>();
        foreach (string file in EnumerateAudioFiles(root))
        {
            try
            {
                using FileStream stream = File.OpenRead(file);
                IReadOnlyDictionary<string, byte[]> frames = Id3GeobReader.ReadGeobFrames(stream);

                bool hasMarkers = frames.TryGetValue(Markers2, out byte[]? markersPayload);
                bool hasGrid = frames.TryGetValue(BeatGrid, out byte[]? gridPayload);
                if (!hasMarkers && !hasGrid)
                    continue; // no Serato data in this file — nothing to import

                IReadOnlyList<ImportedCue>? cues = hasMarkers ? MapCues(markersPayload!) : null;
                SeratoGrid? grid = hasGrid ? SeratoBeatGridReader.Read(gridPayload!) : null;

                tracks.Add(new ImportedTrack(
                    SourcePath: file,
                    Bpm: grid?.Bpm,
                    FirstBeatSeconds: grid?.FirstBeatSeconds,
                    Cues: cues));
            }
            catch (Exception)
            {
                // Skip a file we can't open/parse — one bad file never aborts the import.
            }
        }
        return tracks;
    }

    private static IReadOnlyList<ImportedCue> MapCues(byte[] markersPayload) =>
        SeratoMarkers2Reader.ReadCues(markersPayload)
            .Select(c => new ImportedCue(c.Index, c.PositionMs / 1000.0, c.Name, c.Color))
            .ToList();

    private static IEnumerable<string> EnumerateAudioFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            yield break; // an unreadable tree yields nothing rather than throwing
        }

        foreach (string file in files)
            if (Id3AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
    }

    private static IReadOnlyList<ImportedPlaylist> ReadCrates(string root)
    {
        var playlists = new List<ImportedPlaylist>();
        foreach (string dir in new[] { Path.Combine(root, "_Serato_", "Subcrates"), Path.Combine(root, "Subcrates") })
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string crate in SafeEnumerate(dir, "*.crate"))
            {
                try
                {
                    IReadOnlyList<string> relative = SeratoCrateReader.ReadTrackPaths(File.ReadAllBytes(crate));
                    if (relative.Count == 0)
                        continue;

                    // Crate paths are volume-root-relative; prepend the picked root. Even if that isn't the
                    // exact volume root, the import path resolver remaps by filename against the catalog.
                    var paths = relative.Select(r => Path.Combine(root, NormalizeRelative(r))).ToList();
                    string name = Path.GetFileNameWithoutExtension(crate).Replace("%%", " / ");
                    playlists.Add(new ImportedPlaylist(name, paths));
                }
                catch (Exception)
                {
                    // Skip an unreadable crate.
                }
            }
        }
        return playlists;
    }

    private static string NormalizeRelative(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern);
        }
        catch (Exception)
        {
            return Enumerable.Empty<string>();
        }
    }
}
