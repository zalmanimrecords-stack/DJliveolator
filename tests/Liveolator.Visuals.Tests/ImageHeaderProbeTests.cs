using System.Buffers.Binary;
using Liveolator.Core.Library.Visual;
using Liveolator.Visuals;

namespace Liveolator.Visuals.Tests;

/// <summary>
/// Verifies <see cref="ImageHeaderProbe"/> against hand-crafted headers for each supported
/// format, plus the video and malformed-input contracts the <see cref="VisualMediaLibrary"/>
/// relies on. Each test writes a tiny header to a temp file via <see cref="TempFile"/>.
/// </summary>
public sealed class ImageHeaderProbeTests
{
    private readonly ImageHeaderProbe _probe = new();

    [Fact]
    public async Task Png_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".png", BuildPng(640, 480));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(640, info.Width);
        Assert.Equal(480, info.Height);
        Assert.Null(info.Duration);
    }

    [Fact]
    public async Task Gif_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".gif", BuildGif(320, 200));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(320, info.Width);
        Assert.Equal(200, info.Height);
        Assert.Null(info.Duration);
    }

    [Fact]
    public async Task Bmp_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".bmp", BuildBmp(128, 96));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(128, info.Width);
        Assert.Equal(96, info.Height);
        Assert.Null(info.Duration);
    }

    [Fact]
    public async Task Bmp_TopDown_NegativeHeightIsNormalized()
    {
        using var file = TempFile.WithBytes(".bmp", BuildBmp(128, -96));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(128, info.Width);
        Assert.Equal(96, info.Height);
    }

    [Fact]
    public async Task Jpeg_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".jpg", BuildJpeg(800, 600));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(800, info.Width);
        Assert.Equal(600, info.Height);
        Assert.Null(info.Duration);
    }

    [Fact]
    public async Task WebpLossy_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".webp", BuildWebpLossy(400, 300));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(400, info.Width);
        Assert.Equal(300, info.Height);
    }

    [Fact]
    public async Task WebpLossless_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".webp", BuildWebpLossless(256, 144));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(256, info.Width);
        Assert.Equal(144, info.Height);
    }

    [Fact]
    public async Task WebpExtended_ReadsWidthAndHeight()
    {
        using var file = TempFile.WithBytes(".webp", BuildWebpExtended(1920, 1080));

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Image);

        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
    }

    [Fact]
    public async Task Video_ReturnsUnknownDimensionsWithoutThrowing()
    {
        // Content is irrelevant: video probing is deferred to a future FFmpeg binding.
        using var file = TempFile.WithBytes(".mp4", new byte[] { 0, 1, 2, 3 });

        VisualMediaInfo info = await _probe.ProbeAsync(file.Path, VisualMediaKind.Video);

        Assert.Equal(0, info.Width);
        Assert.Equal(0, info.Height);
        Assert.Null(info.Duration);
    }

    [Fact]
    public async Task GarbageImage_ThrowsInvalidData()
    {
        using var file = TempFile.WithBytes(".png", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33 });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _probe.ProbeAsync(file.Path, VisualMediaKind.Image));
    }

    [Fact]
    public async Task EmptyFile_ThrowsInvalidData()
    {
        using var file = TempFile.WithBytes(".png", Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _probe.ProbeAsync(file.Path, VisualMediaKind.Image));
    }

    // --- header builders -------------------------------------------------------------------

    private static byte[] BuildPng(int width, int height)
    {
        byte[] bytes = new byte[24];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(bytes, 0);
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static byte[] BuildGif(ushort width, ushort height)
    {
        byte[] bytes = new byte[13];
        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), height);
        return bytes;
    }

    private static byte[] BuildBmp(int width, int height)
    {
        byte[] bytes = new byte[54]; // BITMAPFILEHEADER (14) + BITMAPINFOHEADER (40)
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), height);
        return bytes;
    }

    private static byte[] BuildJpeg(ushort width, ushort height)
    {
        // SOI, a small APP0 segment to exercise marker skipping, then a SOF0 frame header.
        var list = new List<byte> { 0xFF, 0xD8 };

        // APP0: marker + length(2) + 4 bytes payload.
        list.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x06, 0x4A, 0x46, 0x49, 0x46 });

        // SOF0: marker + length(2) + precision(1) + height(2) + width(2) + components(1).
        list.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08 });
        byte[] dims = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(dims.AsSpan(0, 2), height);
        BinaryPrimitives.WriteUInt16BigEndian(dims.AsSpan(2, 2), width);
        list.AddRange(dims);
        list.Add(0x03);
        return list.ToArray();
    }

    private static byte[] BuildWebpLossy(ushort width, ushort height)
    {
        byte[] bytes = new byte[30];
        WriteRiffWebpHeader(bytes, "VP8 ");
        // 3-byte frame tag (16..18), start code (19..21), then 14-bit dims at 26/28.
        bytes[20] = 0x9D;
        bytes[21] = 0x01;
        bytes[22] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26, 2), (ushort)(width & 0x3FFF));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), (ushort)(height & 0x3FFF));
        return bytes;
    }

    private static byte[] BuildWebpLossless(int width, int height)
    {
        byte[] bytes = new byte[30];
        WriteRiffWebpHeader(bytes, "VP8L");
        bytes[20] = 0x2F; // VP8L signature
        uint packed = (uint)((width - 1) & 0x3FFF) | ((uint)((height - 1) & 0x3FFF) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21, 4), packed);
        return bytes;
    }

    private static byte[] BuildWebpExtended(int width, int height)
    {
        byte[] bytes = new byte[30];
        WriteRiffWebpHeader(bytes, "VP8X");
        int w = width - 1;
        int h = height - 1;
        bytes[24] = (byte)(w & 0xFF);
        bytes[25] = (byte)((w >> 8) & 0xFF);
        bytes[26] = (byte)((w >> 16) & 0xFF);
        bytes[27] = (byte)(h & 0xFF);
        bytes[28] = (byte)((h >> 8) & 0xFF);
        bytes[29] = (byte)((h >> 16) & 0xFF);
        return bytes;
    }

    private static void WriteRiffWebpHeader(byte[] bytes, string fourcc)
    {
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        for (int i = 0; i < 4; i++)
            bytes[12 + i] = (byte)fourcc[i];
    }
}
