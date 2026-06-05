using System.IO;
using Liveolator.Core.Library;

namespace Liveolator.App.Features.Libraries;

/// <summary>Display wrapper over a <see cref="FolderCatalogSummary"/> for the folder-status window.</summary>
public sealed class FolderStatusViewModel
{
    private readonly FolderCatalogSummary _summary;

    public FolderStatusViewModel(FolderCatalogSummary summary)
        => _summary = summary ?? throw new ArgumentNullException(nameof(summary));

    /// <summary>Full folder path (shown muted under the name).</summary>
    public string Folder => _summary.Folder;

    /// <summary>Last path segment, for the primary label.</summary>
    public string Name
    {
        get
        {
            string trimmed = _summary.Folder.Replace('\\', '/').TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            string name = slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
            return string.IsNullOrEmpty(name) ? _summary.Folder : name;
        }
    }

    public string TrackCountText => _summary.TrackCount switch
    {
        0 => "No tracks",
        1 => "1 track",
        var n => $"{n} tracks",
    };

    /// <summary>"128 tracks · 3 failed · 12 low-confidence" — appends issue counts only when non-zero.</summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string> { TrackCountText };
            if (_summary.Failed > 0)
                parts.Add($"{_summary.Failed} failed");
            if (_summary.PartiallyAnalyzed > 0)
                parts.Add($"{_summary.PartiallyAnalyzed} low-confidence");
            return string.Join(" · ", parts);
        }
    }
}
