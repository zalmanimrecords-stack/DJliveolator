using System.Collections.Generic;
using System.Text;

namespace Liveolator.Media.Import.Engine;

/// <summary>One hot cue from an Engine quickCues BLOB: pad index, sample offset, RGB color, label.</summary>
internal readonly record struct EngineCue(int Index, double SampleOffset, int? Color, string? Label);

/// <summary>
/// Decodes an inflated Engine DJ "quickCues" BLOB. Layout: <c>count</c> (int64 big-endian, normally 8),
/// then per cue: label length (uint8; 0 = unset), label bytes, sample offset (double <em>big-endian</em>,
/// in SAMPLES; −1 = unset), then 4 color bytes in <c>A,R,G,B</c> order. Trailing main-cue doubles follow
/// the array and are ignored. An unset cue (label-len 0 + offset &lt; 0) is skipped.
/// </summary>
internal static class EngineQuickCuesReader
{
    public static IReadOnlyList<EngineCue> Read(byte[] inflated)
    {
        var cues = new List<EngineCue>();
        if (inflated.Length < 8)
            return cues;

        long count = EngineBlob.ReadInt64BE(inflated, 0);
        int p = 8;
        for (long i = 0; i < count; i++)
        {
            if (p >= inflated.Length)
                break;

            int labelLength = inflated[p++];
            string? label = null;
            if (labelLength > 0)
            {
                if (p + labelLength > inflated.Length)
                    break;
                label = Encoding.UTF8.GetString(inflated, p, labelLength);
                p += labelLength;
            }

            if (p + 12 > inflated.Length) // 8-byte offset + 4 color bytes
                break;
            double sampleOffset = EngineBlob.ReadDoubleBE(inflated, p);
            p += 8;
            int r = inflated[p + 1], g = inflated[p + 2], b = inflated[p + 3]; // skip alpha at p+0
            p += 4;

            if (labelLength == 0 && sampleOffset < 0)
                continue; // unset slot

            cues.Add(new EngineCue((int)i, sampleOffset, (r << 16) | (g << 8) | b, label));
        }

        return cues;
    }
}
