using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist.LocalCopy;

/// <summary>What taking one playlist entry offline will do.</summary>
public enum LocalCopyStep
{
    /// <summary>The file will be copied to <see cref="LocalCopyItem.DestinationPath"/>.</summary>
    Copy,

    /// <summary>A file of the catalogued size is already at the destination; it is reused, not recopied.</summary>
    AlreadyPresent,

    /// <summary>The path has no catalog row, so there is no analysis to carry and nothing is copied.</summary>
    NotCatalogued,

    /// <summary>Another entry from a different folder already claims the same destination path.</summary>
    NameClash,

    /// <summary>The path has a <c>.</c> or <c>..</c> segment, so its copy could land outside the destination.</summary>
    UnsafePath,
}

/// <param name="SourcePath">The playlist entry as stored (usually a network path).</param>
/// <param name="DestinationPath">Where the local copy lives, or null when the entry is skipped.</param>
/// <param name="Step">What will happen to the entry.</param>
/// <param name="Track">The catalog row carrying the analysis, or null when not catalogued.</param>
public sealed record LocalCopyItem(string SourcePath, string? DestinationPath, LocalCopyStep Step, MusicTrack? Track);
