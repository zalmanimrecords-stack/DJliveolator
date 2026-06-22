using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import;

/// <summary>
/// Parses a VirtualDJ <c>database.xml</c> into the source-agnostic <see cref="LibraryImport"/>.
/// </summary>
/// <remarks>
/// Shape: <c>VirtualDJ_Database/Song</c> (attr <c>FilePath</c> = raw absolute path) with children
/// <c>Tags</c> (Author/Title/Album/Genre/Year/Bpm/Key), <c>Infos</c> (<c>SongLength</c> seconds),
/// <c>Scan</c> (<c>Bpm</c> = <em>seconds-per-beat</em>, so BPM = 60 / value), and <c>Poi</c> points
/// (<c>Pos</c> seconds). A hot cue is a Poi with <em>no</em> <c>Type</c> and a 1-based <c>Num</c>; a Poi
/// <c>Type="beatgrid"</c> is the grid anchor; <c>automix</c>/loop Pois are skipped. Playlists live in
/// separate <c>*.m3u</c>/<c>*.vdjfolder</c> files (not this XML), so none are imported here. Tolerant —
/// a malformed song/Poi is skipped, never fatal (global standards #16/#26).
/// </remarks>
public sealed class VirtualDjXmlImporter : ILibraryImporter
{
    public string FormatName => "VirtualDJ";

    public LibraryImport Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument doc = XDocument.Load(stream);
        if (doc.Root is null)
            return LibraryImport.Empty;

        var tracks = doc.Root.Elements("Song")
            .Select(ParseSong)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        return new LibraryImport(tracks, Array.Empty<ImportedPlaylist>());
    }

    private static ImportedTrack? ParseSong(XElement song)
    {
        string? path = Trimmed((string?)song.Attribute("FilePath"));
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal))
            return null; // skip net-stream / non-local entries

        XElement? tags = song.Element("Tags");
        XElement? scan = song.Element("Scan");

        double? gridSeconds = null;
        var cues = new List<ImportedCue>();
        foreach (XElement poi in song.Elements("Poi"))
        {
            double? pos = ParseDouble((string?)poi.Attribute("Pos"));
            if (pos is null)
                continue;
            string? type = Trimmed((string?)poi.Attribute("Type"));

            if (string.Equals(type, "beatgrid", StringComparison.OrdinalIgnoreCase))
            {
                gridSeconds ??= pos;
                continue;
            }
            if (type is not null)
                continue; // automix / loop / action POIs are not hot cues

            // A hot cue: no Type, a 1-based Num slot → our 0-based index (out-of-range is dropped later). A
            // POI with no/zero Num is an unnumbered marker, not hot cue 1 — route it to the memory/primary
            // cue rather than collapsing every such POI onto slot 0 (which would overwrite each other and
            // collide with a real Num=1 cue).
            int num = ParseInt((string?)poi.Attribute("Num")) ?? 0;
            int index = num > 0 ? num - 1 : ImportedCue.MemoryCue;
            cues.Add(new ImportedCue(index, pos.Value, Trimmed((string?)poi.Attribute("Name"))));
        }

        return new ImportedTrack(
            SourcePath: path,
            Title: Trimmed((string?)tags?.Attribute("Title")),
            Artist: Trimmed((string?)tags?.Attribute("Author")),
            Album: Trimmed((string?)tags?.Attribute("Album")),
            Genre: Trimmed((string?)tags?.Attribute("Genre")),
            Year: ParseInt((string?)tags?.Attribute("Year")),
            DurationSeconds: ParseDouble((string?)song.Element("Infos")?.Attribute("SongLength")),
            Bpm: ResolveBpm(scan, tags),
            FirstBeatSeconds: gridSeconds,
            Key: Trimmed((string?)tags?.Attribute("Key")) ?? Trimmed((string?)scan?.Attribute("Key")),
            Cues: cues);
    }

    // Scan@Bpm is SECONDS-PER-BEAT (BPM = 60 / value) — the format's #1 gotcha. Fall back to Tags@Bpm,
    // which is already a real BPM.
    private static double? ResolveBpm(XElement? scan, XElement? tags)
    {
        double? secondsPerBeat = ParseDouble((string?)scan?.Attribute("Bpm"));
        if (secondsPerBeat is > 0)
            return 60.0 / secondsPerBeat.Value;
        return ParseDouble((string?)tags?.Attribute("Bpm"));
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;
}
