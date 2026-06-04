using System.IO;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;

namespace Liveolator.Core.Library.Music;

/// <summary>A catalogued music file with its offline analysis (BPM + musical key/scale).</summary>
public sealed record MusicTrack(
    ScannedFile File,
    BpmResult? Bpm,
    MusicalKey? Key,
    MediaAnalysisStatus Status,
    string? Error) : IMediaEntry
{
    /// <summary>Display title derived from the file name.</summary>
    public string Title => Path.GetFileNameWithoutExtension(File.Path);
}
