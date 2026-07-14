using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;

namespace Liveolator.Visuals;

/// <summary>
/// The thumbnail renderer the application injects: routes images to <see cref="ImageThumbnailRenderer"/>
/// (managed Skia decode) and video to <see cref="FfmpegFrameThumbnailRenderer"/> (an ffmpeg-extracted
/// frame). Mirrors <see cref="CompositeVisualMediaProbe"/> so the common image case needs no external
/// tool while a video preview is available when FFmpeg is installed.
/// </summary>
public sealed class CompositeVisualThumbnailRenderer : IVisualThumbnailRenderer
{
    private readonly IVisualThumbnailRenderer _image;
    private readonly IVisualThumbnailRenderer _video;

    public CompositeVisualThumbnailRenderer(string? ffmpegPath = null)
        : this(new ImageThumbnailRenderer(), new FfmpegFrameThumbnailRenderer(ffmpegPath)) { }

    public CompositeVisualThumbnailRenderer(IVisualThumbnailRenderer image, IVisualThumbnailRenderer video)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _video = video ?? throw new ArgumentNullException(nameof(video));
    }

    public Task<VisualPreviewFrame?> RenderAsync(
        string filePath, VisualMediaKind kind, int maxEdge, CancellationToken cancellationToken = default)
        => kind == VisualMediaKind.Video
            ? _video.RenderAsync(filePath, kind, maxEdge, cancellationToken)
            : _image.RenderAsync(filePath, kind, maxEdge, cancellationToken);
}
