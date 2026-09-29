using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist.LocalCopy;

/// <summary>An entry that did not make it to the local copy, and why, in words the DJ can act on.</summary>
public sealed record LocalCopyProblem(string SourcePath, string Reason);

/// <param name="LocalPlaylist">The saved "(local)" playlist, or null when nothing was taken offline.</param>
/// <param name="LocalTracks">Catalog rows newly saved for local files; the caller merges them into its in-memory library.</param>
/// <param name="Copied">Files copied in this run.</param>
/// <param name="AlreadyPresent">Entries whose local file was already there and was reused.</param>
/// <param name="Problems">Entries left out, each with a reason.</param>
/// <param name="BytesToCopy">Bytes this run needed to copy.</param>
/// <param name="BytesFree">Free bytes at the destination before copying.</param>
public sealed record LocalCopyResult(
    Playlist? LocalPlaylist,
    IReadOnlyList<MusicTrack> LocalTracks,
    int Copied,
    int AlreadyPresent,
    IReadOnlyList<LocalCopyProblem> Problems,
    long BytesToCopy,
    long BytesFree)
{
    /// <summary>True when the run stopped before copying anything because the destination is too small.</summary>
    public bool InsufficientSpace => BytesToCopy > BytesFree;
}
