using System;
using System.Collections.Generic;
using System.Text;

namespace Liveolator.Media.Import.Serato;

/// <summary>One cue parsed from a "Serato Markers2" payload: slot index, position in ms, RGB color, name.</summary>
internal readonly record struct SeratoCue(int Index, long PositionMs, int? Color, string? Name);

/// <summary>
/// Decodes the "Serato Markers2" GEOB payload (hot cues / loops / track color). Layout (clean-room from
/// the public Serato format docs): outer <c>01 01</c> + base64 text (LF every 72 chars, padding stripped,
/// null-padded) up to the first <c>0x00</c>; the base64 decodes to inner <c>01 01</c> + a sequence of
/// name-tagged entries (null-terminated name, uint32 big-endian length, body) ending at a <c>0x00</c>.
/// A CUE body is: pad, index(u8), position(u32 BE, ms), pad, RGB(3), pad×2, null-terminated UTF-8 name.
/// Tolerant — a malformed payload yields no cues rather than throwing.
/// </summary>
internal static class SeratoMarkers2Reader
{
    public static IReadOnlyList<SeratoCue> ReadCues(byte[] payload)
    {
        var cues = new List<SeratoCue>();
        byte[]? inner = DecodeInner(payload);
        if (inner is null)
            return cues;

        int p = 2; // skip the inner 01 01 version
        while (p < inner.Length)
        {
            int nameStart = p;
            while (p < inner.Length && inner[p] != 0)
                p++;
            if (p >= inner.Length)
                break;
            string name = Encoding.ASCII.GetString(inner, nameStart, p - nameStart);
            p++; // consume the name's null terminator
            if (name.Length == 0)
                break; // empty name = end-of-entries terminator

            if (p + 4 > inner.Length)
                break;
            int len = ReadInt32BE(inner, p);
            p += 4;
            if (len < 0 || p + len > inner.Length)
                break;

            if (name == "CUE" && len >= 12)
                cues.Add(ParseCue(inner, p, len));

            p += len; // skip the body (parsed or not)
        }

        return cues;
    }

    private static SeratoCue ParseCue(byte[] inner, int bodyStart, int len)
    {
        int index = inner[bodyStart + 1];
        long positionMs = (uint)ReadInt32BE(inner, bodyStart + 2);
        int color = (inner[bodyStart + 7] << 16) | (inner[bodyStart + 8] << 8) | inner[bodyStart + 9];

        int nameStart = bodyStart + 12;
        int bodyEnd = bodyStart + len;
        int nameEnd = nameStart;
        while (nameEnd < bodyEnd && inner[nameEnd] != 0)
            nameEnd++;
        string cueName = nameEnd > nameStart ? Encoding.UTF8.GetString(inner, nameStart, nameEnd - nameStart) : string.Empty;

        return new SeratoCue(index, positionMs, color, string.IsNullOrEmpty(cueName) ? null : cueName);
    }

    // Returns the decoded inner payload (starting with its 01 01 version), or null when the payload isn't a
    // valid Markers2 blob.
    private static byte[]? DecodeInner(byte[] payload)
    {
        if (payload.Length < 2 || payload[0] != 0x01 || payload[1] != 0x01)
            return null;

        int zero = Array.IndexOf(payload, (byte)0, 2);
        int end = zero < 0 ? payload.Length : zero;
        if (end <= 2)
            return null;

        string base64 = Encoding.ASCII.GetString(payload, 2, end - 2).Replace("\n", string.Empty).Replace("\r", string.Empty);
        try
        {
            byte[] inner = Convert.FromBase64String(RepadBase64(base64));
            return inner.Length >= 2 ? inner : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Serato stores base64 without proper padding: len%4==1 needs "A==", otherwise "=" × (-len % 4).
    private static string RepadBase64(string base64) => (base64.Length % 4) switch
    {
        1 => base64 + "A==",
        2 => base64 + "==",
        3 => base64 + "=",
        _ => base64,
    };

    private static int ReadInt32BE(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
