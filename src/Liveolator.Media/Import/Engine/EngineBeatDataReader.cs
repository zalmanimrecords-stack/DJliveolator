using System;
using System.Collections.Generic;

namespace Liveolator.Media.Import.Engine;

/// <summary>An Engine beat grid reduced to what Liveolator stores: sample rate, BPM, first-beat anchor.</summary>
internal readonly record struct EngineGrid(double SampleRate, double Bpm, double FirstBeatSeconds);

/// <summary>
/// Decodes an inflated Engine DJ "beatData" BLOB. Header (all <em>big-endian</em>): sampleRate (double),
/// total samples (double), isBeatgridSet (uint8). Then two grids — the analyzed "default" then the
/// user-"adjusted" — each: marker count (int64 BE) + that many 24-byte markers whose fields are
/// <em>little-endian</em>: sampleOffset (double LE), beatNumber (int64 LE), beatsToNext (int32 LE),
/// unknown (int32 LE). The mixed header-BE / marker-LE endianness is the format's chief footgun. BPM is
/// derived from the marker span; the anchor is extrapolated back to beat 0.
/// </summary>
internal static class EngineBeatDataReader
{
    private const int MarkerSize = 24;

    private readonly record struct Marker(double SampleOffset, long BeatNumber);

    public static EngineGrid? Read(byte[] inflated)
    {
        if (inflated.Length < 17)
            return null;

        double sampleRate = EngineBlob.ReadDoubleBE(inflated, 0);
        if (sampleRate <= 0)
            return null;

        int p = 17; // skip sampleRate(8) + samples(8) + isBeatgridSet(1)
        List<Marker>? defaultGrid = ReadGrid(inflated, ref p);
        List<Marker>? adjustedGrid = ReadGrid(inflated, ref p);

        List<Marker>? grid = adjustedGrid is { Count: >= 2 } ? adjustedGrid : defaultGrid;
        if (grid is not { Count: >= 2 })
            return null;

        Marker first = grid[0];
        Marker last = grid[^1];
        double beatSpan = last.BeatNumber - first.BeatNumber;
        double sampleSpan = last.SampleOffset - first.SampleOffset;
        if (beatSpan == 0 || sampleSpan <= 0)
            return null;

        double bpm = sampleRate * 60.0 * beatSpan / sampleSpan;
        double samplesPerBeat = sampleRate * 60.0 / bpm;
        double anchorSamples = first.SampleOffset + (0 - first.BeatNumber) * samplesPerBeat;
        double firstBeatSeconds = Math.Max(0.0, anchorSamples / sampleRate);
        return new EngineGrid(sampleRate, bpm, firstBeatSeconds);
    }

    private static List<Marker>? ReadGrid(byte[] data, ref int p)
    {
        if (p + 8 > data.Length)
            return null;
        long count = EngineBlob.ReadInt64BE(data, p);
        p += 8;
        // Bound the marker count against the bytes actually remaining. Comparing as a max-that-fits
        // (rather than `p + count * MarkerSize`) avoids the signed-Int64 multiply overflowing on a
        // crafted/corrupt blob — which would wrap negative, pass a `> data.Length` check, and then
        // read off the end of the buffer.
        if (count < 0 || count > (data.Length - p) / MarkerSize)
            return null;

        var markers = new List<Marker>((int)count);
        for (long i = 0; i < count; i++)
        {
            markers.Add(new Marker(EngineBlob.ReadDoubleLE(data, p), EngineBlob.ReadInt64LE(data, p + 8)));
            p += MarkerSize;
        }
        return markers;
    }
}
