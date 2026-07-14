using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Liveolator.Media.Import.Engine;

/// <summary>
/// Shared decoding for Engine DJ's performance BLOBs. Each BLOB is Qt-<c>qCompress</c> framed:
/// a 4-byte <em>big-endian</em> uncompressed-length prefix followed by a standard zlib stream. The
/// decoded payloads then mix endianness per field (the format's chief footgun), so all multi-byte reads
/// go through the explicit big/little helpers here. Clean-room from the libdjinterop schema/encoder docs
/// + the Mixxx "Engine Library Format" wiki (documentation, not copied code).
/// </summary>
internal static class EngineBlob
{
    /// <summary>Inflate a qCompress-framed BLOB, or null when it is empty/too short/not inflatable.</summary>
    public static byte[]? Inflate(byte[]? blob)
    {
        if (blob is null || blob.Length <= 4)
            return null;

        int expected = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0, 4));
        if (expected <= 0)
            return null;

        try
        {
            using var input = new MemoryStream(blob, 4, blob.Length - 4, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(expected);
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    public static double ReadDoubleBE(byte[] data, int offset) =>
        BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(offset, 8));

    public static long ReadInt64BE(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(offset, 8));

    public static double ReadDoubleLE(byte[] data, int offset) =>
        BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset, 8));

    public static long ReadInt64LE(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset, 8));
}
