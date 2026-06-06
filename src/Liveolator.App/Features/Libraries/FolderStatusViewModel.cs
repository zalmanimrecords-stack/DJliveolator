using Liveolator.App.Shell;
using Liveolator.Core.Library;
using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Display wrapper over a <see cref="FolderCatalogSummary"/> for the folder-status window, plus the
/// per-folder "is this a samples folder?" toggle (B2). Toggling raises the supplied callback so the
/// owning <see cref="LibrariesViewModel"/> updates the classifier + persists; this VM holds no
/// business logic itself.
/// </summary>
public sealed class FolderStatusViewModel : ViewModelBase
{
    private readonly FolderCatalogSummary _summary;
    private readonly Action<string, bool>? _onSampleFolderChanged;
    private bool _isSampleFolder;

    public FolderStatusViewModel(
        FolderCatalogSummary summary,
        bool isSampleFolder = false,
        Action<string, bool>? onSampleFolderChanged = null)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _onSampleFolderChanged = onSampleFolderChanged;
        _isSampleFolder = isSampleFolder; // seed without firing the callback
    }

    /// <summary>Full folder path (shown muted under the name).</summary>
    public string Folder => _summary.Folder;

    /// <summary>
    /// True when this folder is designated a samples source. Setting it notifies the owner, which
    /// reclassifies the catalog and persists; the seed value passed in the constructor does not.
    /// </summary>
    public bool IsSampleFolder
    {
        get => _isSampleFolder;
        set
        {
            if (_isSampleFolder == value)
                return;
            this.RaiseAndSetIfChanged(ref _isSampleFolder, value);
            _onSampleFolderChanged?.Invoke(_summary.Folder, value);
        }
    }

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
