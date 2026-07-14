using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Liveolator.Media.Import.Serato;

/// <summary>
/// Minimal ID3v2 (2.3/2.4) reader that extracts "GEOB" (General Encapsulated Object) frame payloads by
/// their content-description — exactly what Serato stores its cues/beat-grids in ("Serato Markers2",
/// "Serato BeatGrid"). A self-contained parser (rather than a new tag-library dependency) keeps the
/// binary handling testable and under our control; it reads only the ID3v2 tag region at the file start,
/// never the audio. Handles MP3/AIFF (ID3-tagged) containers; FLAC/MP4 store Serato data differently and
/// are out of scope for this phase.
/// </summary>
internal static class Id3GeobReader
{
    /// <summary>GEOB payloads keyed by content-description; empty when the stream has no ID3v2 tag.</summary>
    public static IReadOnlyDictionary<string, byte[]> ReadGeobFrames(Stream stream)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        byte[] header = new byte[10];
        if (!ReadExact(stream, header, 10) || header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
            return result;

        int major = header[3];
        int tagSize = Synchsafe(header[6], header[7], header[8], header[9]);
        if (tagSize <= 0)
            return result;

        byte[] tag = new byte[tagSize];
        if (!ReadExact(stream, tag, tagSize))
            return result;

        int pos = 0;
        while (pos + 10 <= tag.Length)
        {
            if (tag[pos] == 0) // null frame id = start of padding
                break;

            string id = Encoding.ASCII.GetString(tag, pos, 4);
            int frameSize = major == 4
                ? Synchsafe(tag[pos + 4], tag[pos + 5], tag[pos + 6], tag[pos + 7])
                : (tag[pos + 4] << 24) | (tag[pos + 5] << 16) | (tag[pos + 6] << 8) | tag[pos + 7];
            int bodyStart = pos + 10;
            if (frameSize <= 0 || bodyStart + frameSize > tag.Length)
                break;

            if (id == "GEOB" && TryParseGeob(tag, bodyStart, frameSize, out string desc, out byte[] payload))
                result[desc] = payload;

            pos = bodyStart + frameSize;
        }

        return result;
    }

    // GEOB body: encoding(1) | mime (ISO-8859-1, null) | filename (encoding, null) | description (encoding,
    // null) | binary payload (rest).
    private static bool TryParseGeob(byte[] tag, int start, int length, out string description, out byte[] payload)
    {
        description = string.Empty;
        payload = Array.Empty<byte>();
        int end = start + length;
        int p = start;
        if (p >= end)
            return false;

        byte encoding = tag[p++];
        if (!SkipString(tag, ref p, end, encoding: 0) ||           // MIME is always ISO-8859-1
            !SkipString(tag, ref p, end, encoding) ||              // filename
            !ReadString(tag, ref p, end, encoding, out description)) // content description
            return false;

        payload = tag[p..end];
        return true;
    }

    private static bool SkipString(byte[] data, ref int p, int end, byte encoding)
        => ReadString(data, ref p, end, encoding, out _);

    private static bool ReadString(byte[] data, ref int p, int end, byte encoding, out string value)
    {
        value = string.Empty;
        int start = p;
        if (encoding is 1 or 2) // UTF-16 (with/without BOM): terminated by 00 00 on an even boundary
        {
            while (p + 1 < end && !(data[p] == 0 && data[p + 1] == 0))
                p += 2;
            if (p + 1 >= end)
                return false;
            value = Encoding.Unicode.GetString(data, start, p - start);
            p += 2;
            return true;
        }

        // ISO-8859-1 (0) / UTF-8 (3): single 0x00 terminator.
        while (p < end && data[p] != 0)
            p++;
        if (p >= end)
            return false;
        value = (encoding == 3 ? Encoding.UTF8 : Encoding.Latin1).GetString(data, start, p - start);
        p++;
        return true;
    }

    private static int Synchsafe(byte b0, byte b1, byte b2, byte b3)
        => ((b0 & 0x7F) << 21) | ((b1 & 0x7F) << 14) | ((b2 & 0x7F) << 7) | (b3 & 0x7F);

    private static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0)
                return false;
            read += n;
        }
        return true;
    }
}
