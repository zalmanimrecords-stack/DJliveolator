using System.Buffers.Binary;

namespace Liveolator.Audio.Tests;

/// <summary>Writes minimal, valid WAV files to a temp path for decoder round-trip tests.</summary>
internal static class WavTestFile
{
    /// <summary>Writes interleaved 16-bit PCM samples (values in [-1,1]) and returns the temp path.</summary>
    public static string WritePcm16(float[] interleaved, int channels, int sampleRate)
    {
        var data = new byte[interleaved.Length * 2];
        for (int i = 0; i < interleaved.Length; i++)
        {
            short s = (short)Math.Clamp((int)MathF.Round(interleaved[i] * 32767f), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), s);
        }
        return Write(audioFormat: 1, bitsPerSample: 16, channels, sampleRate, data);
    }

    /// <summary>Writes interleaved 32-bit IEEE-float samples and returns the temp path.</summary>
    public static string WriteFloat32(float[] interleaved, int channels, int sampleRate)
    {
        var data = new byte[interleaved.Length * 4];
        for (int i = 0; i < interleaved.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4, 4), interleaved[i]);
        return Write(audioFormat: 3, bitsPerSample: 32, channels, sampleRate, data);
    }

    private static string Write(int audioFormat, int bitsPerSample, int channels, int sampleRate, byte[] data)
    {
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-{Guid.NewGuid():N}.wav");

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + data.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                         // fmt chunk size
        w.Write((ushort)audioFormat);
        w.Write((ushort)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((ushort)blockAlign);
        w.Write((ushort)bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(data.Length);
        w.Write(data);
        return path;
    }
}
