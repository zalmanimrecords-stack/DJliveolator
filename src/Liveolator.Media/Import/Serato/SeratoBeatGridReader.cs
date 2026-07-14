using System.Buffers.Binary;

namespace Liveolator.Media.Import.Serato;

/// <summary>A Serato beat grid reduced to what Liveolator stores: the first-beat anchor + the BPM.</summary>
internal readonly record struct SeratoGrid(double FirstBeatSeconds, double Bpm);

/// <summary>
/// Decodes the "Serato BeatGrid" GEOB payload. Layout (clean-room from the public docs): version
/// <c>01 00</c>, marker count (u32 BE), then that many 8-byte markers — non-terminal markers are
/// position(float32 BE seconds) + beats-till-next(u32 BE); the final (terminal) marker is
/// position(float32 BE seconds) + BPM(float32 BE) — followed by a single undocumented footer byte.
/// We take the first marker's position as the grid anchor and the terminal marker's BPM.
/// </summary>
internal static class SeratoBeatGridReader
{
    public static SeratoGrid? Read(byte[] payload)
    {
        if (payload.Length < 6 || payload[0] != 0x01 || payload[1] != 0x00)
            return null;

        int count = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(2, 4));
        if (count <= 0)
            return null;

        const int markersStart = 6;
        const int markerSize = 8;
        int terminalStart = markersStart + (count - 1) * markerSize;
        if (terminalStart + markerSize > payload.Length)
            return null;

        double firstBeatSeconds = BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(markersStart, 4));
        double bpm = BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(terminalStart + 4, 4));
        return new SeratoGrid(firstBeatSeconds < 0 ? 0 : firstBeatSeconds, bpm);
    }
}
