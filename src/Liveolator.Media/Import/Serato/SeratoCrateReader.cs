using System.Collections.Generic;
using System.Text;

namespace Liveolator.Media.Import.Serato;

/// <summary>
/// Decodes a Serato <c>.crate</c> file's track paths. The format (clean-room from the Mixxx-wiki
/// description) is a flat TLV stream: 4-char tag + uint32 big-endian length + body. Each <c>otrk</c>
/// record nests its own TLV stream containing a <c>ptrk</c> entry whose body is the track path as
/// UTF-16 <em>big-endian</em> text, relative to the drive/volume root. Column/sort/view tags are skipped.
/// The crate's display name is NOT in the file (it's the filename), so this returns only the paths.
/// </summary>
internal static class SeratoCrateReader
{
    public static IReadOnlyList<string> ReadTrackPaths(byte[] data)
    {
        var paths = new List<string>();
        int p = 0;
        while (p + 8 <= data.Length)
        {
            string tag = Encoding.ASCII.GetString(data, p, 4);
            int len = ReadInt32BE(data, p + 4);
            int bodyStart = p + 8;
            if (len < 0 || bodyStart + len > data.Length)
                break;

            if (tag == "otrk" && FindPathInTrack(data, bodyStart, len) is { } path)
                paths.Add(path);

            p = bodyStart + len;
        }
        return paths;
    }

    // An otrk body is itself a TLV stream; the path lives in its ptrk (UTF-16 BE) entry.
    private static string? FindPathInTrack(byte[] data, int start, int length)
    {
        int end = start + length;
        int p = start;
        while (p + 8 <= end)
        {
            string tag = Encoding.ASCII.GetString(data, p, 4);
            int len = ReadInt32BE(data, p + 4);
            int bodyStart = p + 8;
            if (len < 0 || bodyStart + len > end)
                break;

            if (tag == "ptrk")
            {
                string path = Encoding.BigEndianUnicode.GetString(data, bodyStart, len);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }

            p = bodyStart + len;
        }
        return null;
    }

    private static int ReadInt32BE(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
