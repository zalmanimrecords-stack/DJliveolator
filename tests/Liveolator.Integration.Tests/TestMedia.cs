using System.Text;

namespace Liveolator.Integration.Tests;

/// <summary>Helpers for writing real WAV files and temp folders used by integration tests.</summary>
internal static class TestMedia
{
    public static float[] ClickTrain(double bpm, int sampleRate, double seconds, int clickWidth = 8)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        for (double pos = 0; pos < total; pos += samplesPerBeat)
        {
            int start = (int)pos;
            for (int i = 0; i < clickWidth && start + i < total; i++)
                buffer[start + i] = 1.0f;
        }
        return buffer;
    }

    public static byte[] Pcm16Wav(float[] mono, int sampleRate) => Pcm16Wav(new[] { mono }, sampleRate);

    public static byte[] Pcm16Wav(float[][] channels, int sampleRate)
    {
        int ch = channels.Length;
        int frames = channels[0].Length;
        const int bits = 16;
        int blockAlign = ch * (bits / 8);
        int byteRate = sampleRate * blockAlign;
        int dataLen = frames * blockAlign;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);  // BinaryWriter is little-endian on all platforms
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)ch);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < ch; c++)
            {
                int s = (int)Math.Round(channels[c][f] * 32767f);
                w.Write((short)Math.Clamp(s, short.MinValue, short.MaxValue));
            }
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>A throwaway temp directory that deletes itself on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liveolator-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Write(string relativeName, byte[] content)
    {
        string full = System.IO.Path.Combine(Path, relativeName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
