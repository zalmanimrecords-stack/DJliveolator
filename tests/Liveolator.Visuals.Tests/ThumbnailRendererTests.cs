using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Visuals;
using SkiaSharp;

namespace Liveolator.Visuals.Tests;

/// <summary>
/// Covers the visual-library preview renderers: the image path decodes + downscales a real PNG, the
/// composite routes by kind, and the contracts that keep a missing tool / bad file from crashing the
/// library tab (a null preview, not an exception).
/// </summary>
public sealed class ThumbnailRendererTests
{
    private const int MaxEdge = 64;

    [Fact]
    public async Task Image_DecodesAndDownscalesToMaxEdge()
    {
        using var file = TempFile.WithBytes(".png", BuildPng(200, 100));
        var renderer = new ImageThumbnailRenderer();

        VisualPreviewFrame? frame = await renderer.RenderAsync(file.Path, VisualMediaKind.Image, MaxEdge);

        Assert.NotNull(frame);
        // Longest edge clamped to MaxEdge, aspect (2:1) preserved.
        Assert.Equal(64, frame!.Width);
        Assert.Equal(32, frame.Height);
        Assert.Equal(frame.Width * frame.Height * 4, frame.RgbaPixels.Length);
    }

    [Fact]
    public async Task Image_SmallerThanMaxEdge_IsNotUpscaled()
    {
        using var file = TempFile.WithBytes(".png", BuildPng(20, 10));
        var renderer = new ImageThumbnailRenderer();

        VisualPreviewFrame? frame = await renderer.RenderAsync(file.Path, VisualMediaKind.Image, MaxEdge);

        Assert.NotNull(frame);
        Assert.Equal(20, frame!.Width);
        Assert.Equal(10, frame.Height);
    }

    [Fact]
    public async Task Image_RendererIgnoresVideoKind()
    {
        using var file = TempFile.WithBytes(".png", BuildPng(20, 10));
        var renderer = new ImageThumbnailRenderer();

        Assert.Null(await renderer.RenderAsync(file.Path, VisualMediaKind.Video, MaxEdge));
    }

    [Fact]
    public async Task Image_UndecodableFile_ReturnsNull_NotThrow()
    {
        using var file = TempFile.WithBytes(".png", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var renderer = new ImageThumbnailRenderer();

        Assert.Null(await renderer.RenderAsync(file.Path, VisualMediaKind.Image, MaxEdge));
    }

    [Fact]
    public async Task Video_MissingFfmpeg_ReturnsNull_NotThrow()
    {
        using var file = TempFile.WithBytes(".mp4", new byte[] { 0, 1, 2, 3 });
        var renderer = new FfmpegFrameThumbnailRenderer(ffmpegPath: "liveolator-no-such-ffmpeg");

        Assert.Null(await renderer.RenderAsync(file.Path, VisualMediaKind.Video, MaxEdge));
    }

    [Fact]
    public async Task Composite_RoutesByKind()
    {
        var image = new StubRenderer(VisualMediaKind.Image);
        var video = new StubRenderer(VisualMediaKind.Video);
        var composite = new CompositeVisualThumbnailRenderer(image, video);

        await composite.RenderAsync("x.png", VisualMediaKind.Image, MaxEdge);
        await composite.RenderAsync("x.mp4", VisualMediaKind.Video, MaxEdge);

        Assert.Equal(1, image.Calls);
        Assert.Equal(1, video.Calls);
    }

    // A real PNG so SkiaSharp can actually decode it (the header-only builders elsewhere are not decodable).
    private static byte[] BuildPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(SKColors.SlateBlue);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private sealed class StubRenderer : IVisualThumbnailRenderer
    {
        private readonly VisualMediaKind _expected;
        public StubRenderer(VisualMediaKind expected) => _expected = expected;
        public int Calls { get; private set; }

        public Task<VisualPreviewFrame?> RenderAsync(
            string filePath, VisualMediaKind kind, int maxEdge, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_expected, kind);
            Calls++;
            return Task.FromResult<VisualPreviewFrame?>(null);
        }
    }
}
