using System.Buffers.Binary;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Visuals;

/// <summary>
/// Pure-managed image-dimensions probe (no native deps): reads only the leading header
/// bytes of a file to extract width/height for PNG, JPEG, GIF, BMP and WebP. Implements the
/// <see cref="IVisualMediaProbe"/> seam (doc 12); <see cref="VisualMediaLibrary"/> turns a
/// thrown <see cref="InvalidDataException"/> into a queryable Failed entry.
///
/// Video probing requires frame/container decoding (FFmpeg) and is a follow-up binding;
/// for now <see cref="VisualMediaKind.Video"/> returns unknown dimensions (0, 0, null)
/// rather than throwing, so video files still catalog successfully.
/// </summary>
public sealed class ImageHeaderProbe : IVisualMediaProbe
{
    // Largest header we ever need to inspect. JPEG is scanned incrementally with a smaller
    // window; the others fit comfortably within this prefix.
    private const int HeaderBytes = 64;

    public async Task<VisualMediaInfo> ProbeAsync(
        string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        // FFmpeg-based video probing is a later binding; report unknown dimensions for now.
        if (kind == VisualMediaKind.Video)
            return new VisualMediaInfo(0, 0, Duration: null);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: HeaderBytes, useAsync: true);

        (int width, int height) = await ReadImageDimensionsAsync(stream, filePath, cancellationToken)
            .ConfigureAwait(false);

        return new VisualMediaInfo(width, height, Duration: null);
    }

    private static async Task<(int width, int height)> ReadImageDimensionsAsync(
        Stream stream, string filePath, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderBytes];
        int read = await ReadFullyAsync(stream, header, 0, HeaderBytes, cancellationToken).ConfigureAwait(false);

        // Span-based header parsing is kept in a synchronous helper because a ReadOnlySpan<byte>
        // local cannot live across an await in an async method.
        if (TryReadFixedHeader(header, read, out int width, out int height))
            return (width, height);

        // JPEG dimensions live in a SOFn marker that can sit past the fixed prefix, so it owns
        // the rest of the stream (the bytes already read are passed in to avoid re-reading).
        if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
            return await ReadJpegAsync(stream, header, read, filePath, cancellationToken).ConfigureAwait(false);

        throw new InvalidDataException(
            $"Unrecognized or unsupported image header for '{filePath}'.");
    }

    /// <summary>Parses the formats whose dimensions fit within the fixed header prefix.</summary>
    private static bool TryReadFixedHeader(byte[] header, int length, out int width, out int height)
    {
        var b = new ReadOnlySpan<byte>(header, 0, length);
        if (TryReadPng(b, out width, out height)) return true;
        if (TryReadGif(b, out width, out height)) return true;
        if (TryReadBmp(b, out width, out height)) return true;
        if (TryReadWebp(b, out width, out height)) return true;
        width = height = 0;
        return false;
    }

    // PNG: 8-byte signature, then an IHDR chunk whose body opens with big-endian width/height.
    private static bool TryReadPng(ReadOnlySpan<byte> b, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (b.Length < 24 || !b[..8].SequenceEqual(signature)) return false;
        if (b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return false;

        width = BinaryPrimitives.ReadInt32BigEndian(b.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(b.Slice(20, 4));
        return true;
    }

    // GIF: "GIF87a"/"GIF89a" then a little-endian logical screen descriptor (width, height).
    private static bool TryReadGif(ReadOnlySpan<byte> b, out int width, out int height)
    {
        width = height = 0;
        if (b.Length < 10) return false;
        bool isGif = b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8'
                     && (b[4] == '7' || b[4] == '9') && b[5] == 'a';
        if (!isGif) return false;

        width = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(6, 2));
        height = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(8, 2));
        return true;
    }

    // BMP: "BM" then a DIB header. The classic BITMAPINFOHEADER stores signed 32-bit
    // dimensions (height may be negative for top-down bitmaps); the older BITMAPCOREHEADER
    // uses 16-bit unsigned dimensions.
    private static bool TryReadBmp(ReadOnlySpan<byte> b, out int width, out int height)
    {
        width = height = 0;
        if (b.Length < 18 || b[0] != 'B' || b[1] != 'M') return false;

        uint dibSize = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(14, 4));
        if (dibSize == 12) // BITMAPCOREHEADER
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(18, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(20, 2));
        }
        else if (b.Length >= 26) // BITMAPINFOHEADER and later
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(18, 4));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.Slice(22, 4)));
        }
        else
        {
            return false;
        }
        return true;
    }

    // WebP: RIFF container ("RIFF"...."WEBP") with one of three chunk codecs:
    //   VP8  (lossy)    – dimensions are 14-bit values after a 3-byte start code.
    //   VP8L (lossless) – 14-bit width/height packed into a 32-bit little-endian field.
    //   VP8X (extended) – explicit 24-bit (value-1) canvas size.
    private static bool TryReadWebp(ReadOnlySpan<byte> b, out int width, out int height)
    {
        width = height = 0;
        if (b.Length < 30) return false;
        if (b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return false;
        if (b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return false;

        ReadOnlySpan<byte> fourcc = b.Slice(12, 4);

        if (fourcc.SequenceEqual("VP8 "u8))
        {
            // 16-byte chunk header, then a 3-byte frame tag, then 0x9D 0x01 0x2A, then dims.
            int dimOffset = 20 + 3 + 3; // 0..11 RIFF/WEBP, 12 chunk header start... 26 = dims
            if (b.Length < dimOffset + 4) return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(dimOffset, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(dimOffset + 2, 2)) & 0x3FFF;
            return true;
        }

        if (fourcc.SequenceEqual("VP8L"u8))
        {
            if (b[20] != 0x2F) return false; // VP8L signature byte
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(21, 4));
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (fourcc.SequenceEqual("VP8X"u8))
        {
            // Canvas width/height are stored minus one across 3 bytes each at offset 24.
            int w = b[24] | (b[25] << 8) | (b[26] << 16);
            int h = b[27] | (b[28] << 8) | (b[29] << 16);
            width = w + 1;
            height = h + 1;
            return true;
        }

        return false;
    }

    // JPEG: a stream of marker segments. Walk past APPn/COM/etc. until a Start Of Frame
    // (SOF0..SOF15, excluding the non-frame markers) which carries height then width.
    private static async Task<(int width, int height)> ReadJpegAsync(
        Stream stream, byte[] prefix, int prefixLength, string filePath, CancellationToken cancellationToken)
    {
        // Replay the already-read prefix, then continue from the live stream so we never seek.
        var reader = new SequentialByteReader(stream, prefix, prefixLength);

        // Consume the SOI (0xFFD8) we already matched.
        await reader.SkipAsync(2, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            int marker = await reader.ReadMarkerAsync(cancellationToken).ConfigureAwait(false);
            if (marker < 0)
                throw new InvalidDataException($"JPEG '{filePath}' ended before a frame header.");

            // Standalone markers (RSTn, SOI, EOI, TEM) carry no length and no payload.
            if (marker is 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                continue;

            int length = await reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
            if (length < 2)
                throw new InvalidDataException($"JPEG '{filePath}' has a malformed segment length.");

            bool isSof = marker is >= 0xC0 and <= 0xCF
                         && marker is not (0xC4 or 0xC8 or 0xCC); // DHT, JPG, DAC are not frames
            if (isSof)
            {
                // SOF payload: 1 byte precision, 2 bytes height, 2 bytes width.
                byte[] frame = await reader.ReadBytesAsync(5, cancellationToken).ConfigureAwait(false);
                int height = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(frame, 1, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(frame, 3, 2));
                return (width, height);
            }

            await reader.SkipAsync(length - 2, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReadFullyAsync(
        Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    /// <summary>
    /// Reads a JPEG byte stream forward-only: first drains an already-buffered prefix, then
    /// the underlying stream. Used so JPEG marker scanning never needs seekable input.
    /// </summary>
    private sealed class SequentialByteReader
    {
        private readonly Stream _stream;
        private readonly byte[] _prefix;
        private readonly int _prefixLength;
        private int _prefixPos;

        public SequentialByteReader(Stream stream, byte[] prefix, int prefixLength)
        {
            _stream = stream;
            _prefix = prefix;
            _prefixLength = prefixLength;
        }

        public async Task<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_prefixPos < _prefixLength)
                return _prefix[_prefixPos++];

            byte[] one = new byte[1];
            int n = await _stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            return n == 0 ? -1 : one[0];
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
        {
            byte[] result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                int b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (b < 0)
                    throw new InvalidDataException("JPEG ended mid-segment.");
                result[i] = (byte)b;
            }
            return result;
        }

        public async Task SkipAsync(int count, CancellationToken cancellationToken)
        {
            for (int i = 0; i < count; i++)
            {
                if (await ReadByteAsync(cancellationToken).ConfigureAwait(false) < 0)
                    throw new InvalidDataException("JPEG ended while skipping a segment.");
            }
        }

        public async Task<int> ReadUInt16Async(CancellationToken cancellationToken)
        {
            int hi = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            int lo = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (hi < 0 || lo < 0)
                throw new InvalidDataException("JPEG ended while reading a 16-bit value.");
            return (hi << 8) | lo;
        }

        /// <summary>Advances to the next marker byte (the value after one or more 0xFF fill bytes).</summary>
        public async Task<int> ReadMarkerAsync(CancellationToken cancellationToken)
        {
            int b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            while (b != 0xFF)
            {
                if (b < 0) return -1;
                b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            }
            // Skip any run of 0xFF padding.
            while (b == 0xFF)
                b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            return b;
        }
    }
}
