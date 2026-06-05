using System.IO;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Playlists;

/// <summary>A row in the playlist being built: the track path plus a light display (title / key / BPM).</summary>
public sealed class PlaylistTrackViewModel
{
    public PlaylistTrackViewModel(string path, string title, string key, string bpm)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Title = title;
        Key = key;
        Bpm = bpm;
    }

    /// <summary>Builds a row from a catalogued track, or from the bare path when it is not in the library.</summary>
    public static PlaylistTrackViewModel From(string path, MusicTrack? track)
        => track is null
            ? new PlaylistTrackViewModel(path, System.IO.Path.GetFileNameWithoutExtension(path), "—", "—")
            : new PlaylistTrackViewModel(
                path,
                track.Title,
                track.Key?.Camelot ?? "—",
                track.Bpm is { } b ? b.Bpm.ToString("0.0") : "—");

    public string Path { get; }
    public string Title { get; }
    public string Key { get; }
    public string Bpm { get; }
}
