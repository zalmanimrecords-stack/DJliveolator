using Liveolator.Visuals.Gl;
using SkiaSharp;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class SkiaImageLoaderTests
{
    private static byte[] EncodePng(int width, int height, SKColor fill)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(fill);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Load_decodes_an_image_to_matching_rgba_pixels()
    {
        using var file = TempFile.WithBytes(".png", EncodePng(4, 3, new SKColor(10, 20, 30, 255)));
        var loader = new SkiaImageLoader();

        RgbaImage image = loader.Load(file.Path);

        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(4 * 3 * RgbaImage.BytesPerPixel, image.Pixels.Length);
        // First pixel is the fill color in RGBA order.
        Assert.Equal(10, image.Pixels[0]);
        Assert.Equal(20, image.Pixels[1]);
        Assert.Equal(30, image.Pixels[2]);
        Assert.Equal(255, image.Pixels[3]);
    }

    [Fact]
    public void Load_throws_ImageLoadException_for_a_missing_file()
    {
        var loader = new SkiaImageLoader();

        Assert.Throws<ImageLoadException>(() => loader.Load("does-not-exist-12345.png"));
    }

    [Fact]
    public void Load_throws_ImageLoadException_for_a_non_image_file()
    {
        using var file = TempFile.WithBytes(".png", new byte[] { 1, 2, 3, 4, 5 });
        var loader = new SkiaImageLoader();

        Assert.Throws<ImageLoadException>(() => loader.Load(file.Path));
    }

    [Fact]
    public void Load_rejects_an_empty_path()
    {
        var loader = new SkiaImageLoader();

        Assert.Throws<ArgumentException>(() => loader.Load(" "));
    }
}
