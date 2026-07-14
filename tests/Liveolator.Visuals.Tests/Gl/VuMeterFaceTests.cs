using System;
using System.IO;
using Liveolator.Visuals.Gl;
using SkiaSharp;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class VuMeterFaceTests
{
    [Fact]
    public void Render_ProducesFaceOfExpectedSize()
    {
        using SKBitmap bitmap = VuMeterFace.Render();

        Assert.Equal(VuMeterGeometry.FaceWidth, bitmap.Width);
        Assert.Equal(VuMeterGeometry.FaceHeight, bitmap.Height);

        // Emit the PNG to the test artifacts dir so it can be visually inspected against the reference.
        string dir = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "vu-meter-face.png");
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream file = File.Create(path);
        data.SaveTo(file);

        Assert.True(new FileInfo(path).Length > 0);
    }
}
