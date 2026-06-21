using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import;

/// <summary>
/// Parses a Rekordbox "Export Collection in XML" file (<c>collection.xml</c> / <c>rekordbox.xml</c>) into
/// the source-agnostic <see cref="LibraryImport"/>. Rekordbox's plain XML is the universal interchange
/// format; the encrypted <c>master.db</c> is deliberately not touched. Positions are in seconds.
/// </summary>
/// <remarks>
/// Shape: <c>DJ_PLAYLISTS/COLLECTION/TRACK</c> (attrs: Name/Artist/Album/Genre/Year/AverageBpm/Tonality/
/// TotalTime/Comments/Location URL) with child <c>TEMPO@Inizio</c> (grid anchor, seconds) and
/// <c>POSITION_MARK</c> (cues: <c>Start</c> seconds, <c>Num</c> = hot-cue 0..7 or -1 memory, RGB color);
/// and <c>PLAYLISTS</c>, a NODE tree whose Type="1" leaves list <c>TRACK@Key</c> = TrackID. Tolerant:
/// a malformed track/cue is skipped, never fatal (global standards #16/#26).
/// </remarks>
public sealed class RekordboxXmlImporter : ILibraryImporter
{
    public string FormatName => "Rekordbox";

    public LibraryImport Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument doc = XDocument.Load(stream);

        XElement? collection = doc.Root?.Element("COLLECTION");
        if (collection is null)
            return LibraryImport.Empty;

        var byTrackId = new Dictionary<string, string>(StringComparer.Ordinal); // TrackID -> local path
        var tracks = new List<ImportedTrack>();
        foreach (XElement track in collection.Elements("TRACK"))
        {
            string? path = DecodeLocation((string?)track.Attribute("Location"));
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string? id = (string?)track.Attribute("TrackID");
            if (!string.IsNullOrEmpty(id))
                byTrackId[id] = path;

            tracks.Add(ParseTrack(track, path));
        }

        return new LibraryImport(tracks, ParsePlaylists(doc.Root?.Element("PLAYLISTS"), byTrackId));
    }

    private static ImportedTrack ParseTrack(XElement track, string path)
    {
        double? bpm = ParseDouble((string?)track.Attribute("AverageBpm"));
        double? inizio = ParseDouble((string?)track.Element("TEMPO")?.Attribute("Inizio"));

        return new ImportedTrack(
            SourcePath: path,
            Title: Trimmed((string?)track.Attribute("Name")),
            Artist: Trimmed((string?)track.Attribute("Artist")),
            Album: Trimmed((string?)track.Attribute("Album")),
            Genre: Trimmed((string?)track.Attribute("Genre")),
            Year: ParseInt((string?)track.Attribute("Year")),
            Comment: Trimmed((string?)track.Attribute("Comments")),
            DurationSeconds: ParseDouble((string?)track.Attribute("TotalTime")),
            Bpm: bpm,
            FirstBeatSeconds: inizio,
            Key: Trimmed((string?)track.Attribute("Tonality")),
            Cues: ParseCues(track));
    }

    private static IReadOnlyList<ImportedCue> ParseCues(XElement track)
    {
        var cues = new List<ImportedCue>();
        foreach (XElement mark in track.Elements("POSITION_MARK"))
        {
            // Type 0 = cue/hot cue. Skip loops/fades/load marks (1,2,3,4) in this phase.
            if (ParseInt((string?)mark.Attribute("Type")) is not (null or 0))
                continue;

            double? start = ParseDouble((string?)mark.Attribute("Start"));
            if (start is null)
                continue;

            int num = ParseInt((string?)mark.Attribute("Num")) ?? ImportedCue.MemoryCue;
            int index = num < 0 ? ImportedCue.MemoryCue : num;
            cues.Add(new ImportedCue(index, start.Value, Trimmed((string?)mark.Attribute("Name")), ParseColor(mark)));
        }
        return cues;
    }

    private static int? ParseColor(XElement mark)
    {
        int? r = ParseInt((string?)mark.Attribute("Red"));
        int? g = ParseInt((string?)mark.Attribute("Green"));
        int? b = ParseInt((string?)mark.Attribute("Blue"));
        if (r is null || g is null || b is null)
            return null;
        return ((r.Value & 0xFF) << 16) | ((g.Value & 0xFF) << 8) | (b.Value & 0xFF);
    }

    private static IReadOnlyList<ImportedPlaylist> ParsePlaylists(
        XElement? playlistsRoot, IReadOnlyDictionary<string, string> byTrackId)
    {
        var result = new List<ImportedPlaylist>();
        // The root NODE is the unnamed container; walk its children so its name isn't prefixed.
        XElement? root = playlistsRoot?.Element("NODE");
        if (root is not null)
            foreach (XElement child in root.Elements("NODE"))
                Walk(child, prefix: null, byTrackId, result);
        return result;
    }

    private static void Walk(
        XElement node, string? prefix, IReadOnlyDictionary<string, string> byTrackId, List<ImportedPlaylist> result)
    {
        string name = (string?)node.Attribute("Name") ?? string.Empty;
        string fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix} / {name}";

        // Type 1 = playlist (leaf); Type 0 = folder (recurse).
        if (((string?)node.Attribute("Type")) == "1")
        {
            var paths = new List<string>();
            foreach (XElement entry in node.Elements("TRACK"))
            {
                string? key = (string?)entry.Attribute("Key");
                if (key is not null && byTrackId.TryGetValue(key, out string? path))
                    paths.Add(path);
            }
            if (paths.Count > 0)
                result.Add(new ImportedPlaylist(fullName, paths));
            return;
        }

        foreach (XElement child in node.Elements("NODE"))
            Walk(child, fullName, byTrackId, result);
    }

    // Rekordbox stores a file:// URL (URL-encoded, "localhost" host). Uri handles both Windows and macOS
    // paths; fall back to a manual unescape if it isn't a valid URI.
    private static string? DecodeLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;
        if (Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            return uri.LocalPath;
        return Uri.UnescapeDataString(location);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;
}
