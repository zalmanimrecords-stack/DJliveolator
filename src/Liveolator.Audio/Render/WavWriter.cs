using System.Buffers.Binary;

namespace Liveolator.Audio.Render;

/// <summary>
/// Writes float PCM to a 16-bit little-endian WAV file - the offline mixdown output (doc: STUDIO
/// render). Mono via <see cref="WriteMono"/> and interleaved stereo via <see cref="WriteStereo"/>. Pure
/// managed IO with no native dependency; samples are clamped to [-1, 1] before quantizing. MP3 export is
/// layered on top later via the FFmpeg CLI.
/// </summary>
public static class WavWriter
{
    private const int BitsPerSample = 16;
    private const short PcmFormat = 1;

    /// <summary>Write <paramref name="samples"/> (mono, float -1..1) as a 16-bit PCM WAV at the given rate.</summary>
    public static void WriteMono(string path, ReadOnlySpan<float> samples, int sampleRate)
        => WriteInterleaved(path, samples, channels: 1, sampleRate);

    /// <summary>
    /// Write <paramref name="left"/>/<paramref name="right"/> (float -1..1) as an interleaved 16-bit PCM
    /// stereo WAV at the given rate - the offline STUDIO mixdown output. The two buffers must be the same
    /// length (one frame = one L sample + one R sample); samples are clamped to [-1, 1] before quantizing.
    /// </summary>
    public static void WriteStereo(string path, ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        if (left.Length != right.Length)
            throw new ArgumentException(
                $"Left ({left.Length}) and right ({right.Length}) channels must be the same length.", nameof(right));

        // Interleave into one buffer (L0,R0,L1,R1,...) so the shared writer emits 2-channel frames.
        var interleaved = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            interleaved[(i * 2) + 0] = left[i];
            interleaved[(i * 2) + 1] = right[i];
        }

        WriteInterleaved(path, interleaved, channels: 2, sampleRate);
    }

    // Shared writer for any channel count: the samples span is already interleaved by frame.
    private static void WriteInterleaved(string path, ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        int bytesPerSample = BitsPerSample / 8;
        int dataBytes = interleaved.Length * bytesPerSample;
        int blockAlign = channels * bytesPerSample;
        int byteRate = sampleRate * blockAlign;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);

        // RIFF / WAVE header
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);   // chunk size = 36 + data
        w.Write("WAVE"u8);
        // fmt subchunk
        w.Write("fmt "u8);
        w.Write(16);               // PCM fmt chunk size
        w.Write(PcmFormat);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)BitsPerSample);
        // data subchunk
        w.Write("data"u8);
        w.Write(dataBytes);

        Span<byte> pair = stackalloc byte[2];
        foreach (float sample in interleaved)
        {
            short s = ToPcm16(sample);
            BinaryPrimitives.WriteInt16LittleEndian(pair, s);
            w.Write(pair);
        }
    }

    private static short ToPcm16(float sample)
    {
        double clamped = Math.Clamp(sample, -1.0, 1.0);
        // Symmetric scaling so full-scale +1 maps to short.MaxValue and -1 to short.MinValue+1.
        return (short)Math.Round(clamped * short.MaxValue);
    }
}
