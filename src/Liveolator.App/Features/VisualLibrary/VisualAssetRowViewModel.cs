using Avalonia.Media.Imaging;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using ReactiveUI;

namespace Liveolator.App.Features.VisualLibrary;

/// <summary>Display wrapper over a <see cref="VisualAsset"/> for the visual-library table and detail panel.</summary>
public sealed class VisualAssetRowViewModel : ReactiveObject
{
    private const string None = "—";

    private Bitmap? _thumbnail;

    public VisualAssetRowViewModel(VisualAsset asset)
        => Asset = asset ?? throw new ArgumentNullException(nameof(asset));

    public VisualAsset Asset { get; }

    /// <summary>The lazily-rendered grid thumbnail, or null until <see cref="VisualLibraryViewModel.EnsureThumbnailAsync"/>
    /// has produced one (the tile shows <see cref="KindGlyph"/> until then, and stays on the glyph if no
    /// preview can be made). Set on the UI thread once.</summary>
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
    }

    // --- table columns ---
    public string Title => Asset.Title;

    /// <summary>"Image" / "Video" — drives the kind column and the kind-glyph fallback.</summary>
    public string KindText => Asset.Kind == VisualMediaKind.Video ? "Video" : "Image";

    /// <summary>A compact glyph standing in for a thumbnail (cheap, no decode).</summary>
    public string KindGlyph => Asset.Kind == VisualMediaKind.Video ? "▶" : "▦";

    /// <summary>"WxH" pixels, or "—" when the probe failed.</summary>
    public string Dimensions => Asset.Info is { } info ? $"{info.Width}×{info.Height}" : None;

    /// <summary>"m:ss" for videos; "—" for still images or a failed probe.</summary>
    public string Duration =>
        Asset.Info?.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : None;

    public string FileName => System.IO.Path.GetFileName(Asset.File.Path);

    public MediaAnalysisStatus Status => Asset.Status;

    public string StatusText => Asset.Status switch
    {
        MediaAnalysisStatus.Ok => "OK",
        MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
        _ => "Failed",
    };

    // --- detail panel ---

    /// <summary>"folder · kind" — omits unknown parts.</summary>
    public string SubLine
    {
        get
        {
            string? folder = System.IO.Path.GetDirectoryName(Asset.File.Path);
            var parts = new[] { string.IsNullOrEmpty(folder) ? null : folder, KindText };
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string Path => Asset.File.Path;
    public string Error => Asset.Error ?? None;
}
