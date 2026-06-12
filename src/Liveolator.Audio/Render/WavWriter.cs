using System.Buffers.Binary;

namespace Liveolator.Audio.Render;

/// <summary>
/// Writes mono float PCM to a 16-bit little-endian WAV file — the offline mixdown output (doc: STUDIO
/// render). Pure managed IO with no native dependency; samples are clamped to [-1, 1] before
/// quantizing. MP3 export is layered on top later via the FFmpeg CLI.
/// </summary>
public static class WavWriter
{
    private const int BitsPerSample = 16;
    private const short PcmFormat = 1;

    /// <summary>Write <paramref name="samples"/> (mono, float -1..1) as a 16-bit PCM WAV at the given rate.</summary>
    public static void WriteMono(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        const int channels = 1;
        int bytesPerSample = BitsPerSample / 8;
        int dataBytes = samples.Length * bytesPerSample * channels;
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
        foreach (float sample in samples)
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
