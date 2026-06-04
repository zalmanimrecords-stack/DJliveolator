using System.IO;

namespace Liveolator.Core.Library.Visual;

/// <summary>
/// Catalogs visual media files (images + video clips), classifying each by extension and
/// probing dimensions/duration via <see cref="IVisualMediaProbe"/>. Live camera/capture
/// inputs are a separate runtime source, not part of this file library.
/// </summary>
public sealed class VisualMediaLibrary : MediaLibrary<VisualAsset>
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".mpg", ".mpeg"
    };

    private static readonly HashSet<string> AllExtensions =
        new(ImageExtensions.Concat(VideoExtensions), StringComparer.OrdinalIgnoreCase);

    private readonly IVisualMediaProbe _probe;

    public VisualMediaLibrary(IFileEnumerator enumerator, IVisualMediaProbe probe)
        : base(enumerator)
        => _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    protected override IReadOnlySet<string> Extensions => AllExtensions;

    protected override async Task<VisualAsset> CreateEntryAsync(ScannedFile file, CancellationToken cancellationToken)
    {
        VisualMediaKind kind = KindOf(file.Path);
        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, kind, cancellationToken).ConfigureAwait(false);
        return new VisualAsset(file, kind, info, MediaAnalysisStatus.Ok, null);
    }

    protected override VisualAsset CreateFailedEntry(ScannedFile file, string error)
        => new(file, KindOf(file.Path), null, MediaAnalysisStatus.Failed, error);

    /// <summary>All catalogued assets of a given kind.</summary>
    public IReadOnlyList<VisualAsset> OfKind(VisualMediaKind kind)
        => All.Where(a => a.Kind == kind).ToList();

    private static VisualMediaKind KindOf(string path)
        => VideoExtensions.Contains(Path.GetExtension(path)) ? VisualMediaKind.Video : VisualMediaKind.Image;
}
