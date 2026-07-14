using Liveolator.Core.Library.Visual;

namespace Liveolator.Visuals;

/// <summary>
/// The probe the application injects: routes images to the pure-managed
/// <see cref="ImageHeaderProbe"/> (no native dependency) and video to
/// <see cref="FfprobeVideoProbe"/> (ffprobe). Mirrors the audio CompositeAudioDecoder split so
/// the common image case needs no external tool while video metadata is available when FFmpeg is.
/// </summary>
public sealed class CompositeVisualMediaProbe : IVisualMediaProbe
{
    private readonly IVisualMediaProbe _image;
    private readonly IVisualMediaProbe _video;

    public CompositeVisualMediaProbe(string? ffprobePath = null)
        : this(new ImageHeaderProbe(), new FfprobeVideoProbe(ffprobePath)) { }

    public CompositeVisualMediaProbe(IVisualMediaProbe image, IVisualMediaProbe video)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _video = video ?? throw new ArgumentNullException(nameof(video));
    }

    public Task<VisualMediaInfo> ProbeAsync(
        string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
        => kind == VisualMediaKind.Video
            ? _video.ProbeAsync(filePath, kind, cancellationToken)
            : _image.ProbeAsync(filePath, kind, cancellationToken);
}
