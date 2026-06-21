using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import;

/// <summary>
/// Parses a Traktor <c>collection.nml</c> (plain XML) into the source-agnostic <see cref="LibraryImport"/>.
/// Cue positions are in <em>milliseconds</em> (converted to seconds here).
/// </summary>
/// <remarks>
/// Shape: <c>NML/COLLECTION/ENTRY</c> (TITLE/ARTIST attrs; child <c>LOCATION</c> = VOLUME + DIR (Traktor
/// "/:"-separated) + FILE; <c>INFO</c> with GENRE/KEY/COMMENT/PLAYTIME/RELEASE_DATE; <c>TEMPO@BPM</c>;
/// <c>MUSICAL_KEY@VALUE</c> integer 0-23 as a key fallback; <c>CUE_V2</c> markers with START ms,
/// HOTCUE slot (-1 = none), TYPE (0 cue, 4 grid)). Playlists live under <c>PLAYLISTS</c> as nested NODEs
/// whose PLAYLIST/ENTRY/PRIMARYKEY@KEY is a "/:"-separated path. Tolerant: a bad entry is skipped.
/// </remarks>
public sealed class TraktorNmlImporter : ILibraryImporter
{
    public string FormatName => "Traktor";

    public LibraryImport Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument doc = XDocument.Load(stream);

        XElement? collection = doc.Root?.Element("COLLECTION");
        var tracks = collection is null
            ? new List<ImportedTrack>()
            : collection.Elements("ENTRY").Select(ParseEntry).Where(t => t is not null).Select(t => t!).ToList();

        return new LibraryImport(tracks, ParsePlaylists(doc.Root?.Element("PLAYLISTS")));
    }

    private static ImportedTrack? ParseEntry(XElement entry)
    {
        string? path = ReconstructPath(entry.Element("LOCATION"));
        if (string.IsNullOrWhiteSpace(path))
            return null;

        XElement? info = entry.Element("INFO");
        double? gridSeconds = null;
        var cues = new List<ImportedCue>();
        foreach (XElement cue in entry.Elements("CUE_V2"))
        {
            int type = ParseInt((string?)cue.Attribute("TYPE")) ?? 0;
            double? startMs = ParseDouble((string?)cue.Attribute("START"));
            if (startMs is null)
                continue;
            double startSeconds = startMs.Value / 1000.0;

            if (type == 4) // grid marker -> the beat-grid anchor
            {
                gridSeconds ??= startSeconds;
                continue;
            }

            int hot = ParseInt((string?)cue.Attribute("HOTCUE")) ?? -1;
            if (hot >= 0)
                cues.Add(new ImportedCue(hot, startSeconds, Trimmed((string?)cue.Attribute("NAME"))));
            else if (type == 0) // an unindexed cue -> memory/primary cue
                cues.Add(new ImportedCue(ImportedCue.MemoryCue, startSeconds, Trimmed((string?)cue.Attribute("NAME"))));
        }

        return new ImportedTrack(
            SourcePath: path,
            Title: Trimmed((string?)entry.Attribute("TITLE")),
            Artist: Trimmed((string?)entry.Attribute("ARTIST")),
            Album: Trimmed((string?)entry.Element("ALBUM")?.Attribute("TITLE")),
            Genre: Trimmed((string?)info?.Attribute("GENRE")),
            Year: ParseYear((string?)info?.Attribute("RELEASE_DATE")),
            Comment: Trimmed((string?)info?.Attribute("COMMENT")),
            DurationSeconds: ParseDouble((string?)info?.Attribute("PLAYTIME")),
            Bpm: ParseDouble((string?)entry.Element("TEMPO")?.Attribute("BPM")),
            FirstBeatSeconds: gridSeconds,
            Key: ResolveKey(info, entry.Element("MUSICAL_KEY")),
            Cues: cues);
    }

    // Prefer the human key text (INFO@KEY); fall back to the MUSICAL_KEY integer 0-23 (0-11 major C..B,
    // 12-23 minor C..B — the chromatic ordering used by Traktor) mapped to a Camelot code.
    private static string? ResolveKey(XElement? info, XElement? musicalKey)
    {
        string? text = Trimmed((string?)info?.Attribute("KEY"));
        if (!string.IsNullOrEmpty(text))
            return text;

        if (ParseInt((string?)musicalKey?.Attribute("VALUE")) is { } value && value is >= 0 and <= 23)
        {
            int pitchClass = value % 12;
            KeyMode mode = value < 12 ? KeyMode.Major : KeyMode.Minor;
            return Camelot.Code(pitchClass, mode);
        }
        return null;
    }

    private static IReadOnlyList<ImportedPlaylist> ParsePlaylists(XElement? playlistsRoot)
    {
        var result = new List<ImportedPlaylist>();
        if (playlistsRoot is not null)
            foreach (XElement node in playlistsRoot.Elements("NODE"))
                Walk(node, prefix: null, result);
        return result;
    }

    private static void Walk(XElement node, string? prefix, List<ImportedPlaylist> result)
    {
        string type = (string?)node.Attribute("TYPE") ?? string.Empty;
        string name = (string?)node.Attribute("NAME") ?? string.Empty;
        bool isRoot = name == "$ROOT";
        string fullName = isRoot || string.IsNullOrEmpty(prefix) ? name : $"{prefix} / {name}";

        if (type == "PLAYLIST")
        {
            var paths = new List<string>();
            foreach (XElement entry in node.Element("PLAYLIST")?.Elements("ENTRY") ?? Enumerable.Empty<XElement>())
            {
                string? key = (string?)entry.Element("PRIMARYKEY")?.Attribute("KEY");
                string? path = key is null ? null : key.Replace("/:", "/");
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path!);
            }
            if (paths.Count > 0)
                result.Add(new ImportedPlaylist(name, paths));
            return;
        }

        // FOLDER (or the root): recurse into SUBNODES. The root's own name isn't used as a prefix.
        XElement? subnodes = node.Element("SUBNODES");
        if (subnodes is not null)
            foreach (XElement child in subnodes.Elements("NODE"))
                Walk(child, isRoot ? null : fullName, result);
    }

    // Traktor LOCATION = VOLUME + DIR + FILE, where DIR uses "/:" as the separator (e.g. "/:Music/:House/:").
    // Reconstruct a best-effort path; the path resolver remaps it by filename against the local catalog.
    private static string? ReconstructPath(XElement? location)
    {
        if (location is null)
            return null;
        string? file = (string?)location.Attribute("FILE");
        if (string.IsNullOrWhiteSpace(file))
            return null;

        string volume = (string?)location.Attribute("VOLUME") ?? string.Empty;
        string dir = ((string?)location.Attribute("DIR") ?? string.Empty).Replace("/:", "/");
        return $"{volume}{dir}{file}";
    }

    private static int? ParseYear(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
            return null;
        // RELEASE_DATE is "YYYY/M/D"; take the leading year.
        string head = releaseDate.Split('/', '-')[0];
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year) && year > 0
            ? year
            : null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;
}
