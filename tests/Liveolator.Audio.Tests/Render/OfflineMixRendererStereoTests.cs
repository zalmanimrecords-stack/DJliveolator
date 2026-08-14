using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using Liveolator.Audio.Render;
using Liveolator.Core.Studio;
using ManagedBass;
using Xunit;

namespace Liveolator.Audio.Tests.Render;

/// <summary>
/// A clip that needs no warp must still render in STEREO (bug 2026-08-14). The renderer had two decode
/// paths — warped clips through BASS_FX (stereo) and unwarped clips through the analysis decoder, which is
/// mono by design (<c>IAudioDecoder.DecodeMonoAsync</c>) — so any track already at the project tempo was
/// duplicated L=R and lost its stereo image. In the measured 68-minute psytrance export the two tracks
/// whose BPM already matched (140) came out with a bit-exact zero side channel: eleven minutes in mono
/// inside an otherwise stereo mix, and no warning anywhere.
/// <para>CI has no native BASS, where the mono fallback is still the only option, so this test SKIPS when
/// BASS cannot init — the same convention as <c>BassFxNativeSmokeTests</c>.</para>
/// </summary>
public sealed class OfflineMixRendererStereoTests
{
    [Fact]
    public async Task Render_UnwarpedClip_KeepsTheSourceStereoImage()
    {
        if (!BassCanInit())
            return; // no native BASS in this environment — the stereo decode cannot be exercised.

        const int rate = 44_100;
        string source = WriteStereoWav(rate, seconds: 2.0, leftHz: 440, rightHz: 220);
        string output = Path.Combine(Path.GetTempPath(), $"liveolator-stereo-{Guid.NewGuid():N}.wav");
        try
        {
            // SourceBpm == project BPM ⇒ warp factor exactly 1.0 ⇒ the unwarped decode path.
            var project = new StudioProject("p", 140,
                new[] { new StudioClip(0, source, 0, TimeSpan.Zero, TimeSpan.FromSeconds(2), SourceBpm: 140, WarpEnabled: true) },
                Array.Empty<AutomationLane>());

            await new OfflineMixRenderer(new CompositeAudioDecoder()).RenderAsync(project, output, rate);

            (float[] left, float[] right) = ReadWavStereo(output);
            Assert.NotEmpty(left);
            double side = SideRms(left, right);
            Assert.True(side > 0.01, $"side channel RMS {side:F6} — the clip rendered in mono (L == R).");
        }
        finally
        {
            TryDelete(source);
            TryDelete(output);
        }
    }

    private static double SideRms(float[] left, float[] right)
    {
        double sum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            double s = (left[i] - right[i]) / 2.0;
            sum += s * s;
        }

        return Math.Sqrt(sum / Math.Max(1, left.Length));
    }

    private static bool BassCanInit()
    {
        try
        {
            return Bass.Init(0) || Bass.LastError == Errors.Already;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    // Two different tones L/R, so a mono collapse is unmistakable (and neither channel is silent).
    private static string WriteStereoWav(int rate, double seconds, double leftHz, double rightHz)
    {
        int frames = (int)(rate * seconds);
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-stereo-src-{Guid.NewGuid():N}.wav");
        using var w = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write));
        int dataBytes = frames * 4;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)2);            // stereo
        w.Write(rate);
        w.Write(rate * 4);            // byte rate
        w.Write((short)4);            // block align
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)rate;
            w.Write((short)(Math.Sin(t * 2 * Math.PI * leftHz) * 12000));
            w.Write((short)(Math.Sin(t * 2 * Math.PI * rightHz) * 12000));
        }

        return path;
    }

    private static (float[] Left, float[] Right) ReadWavStereo(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int frames = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)) / 4;
        var left = new float[frames];
        var right = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            left[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + (i * 4), 2)) / (float)short.MaxValue;
            right[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + (i * 4) + 2, 2)) / (float)short.MaxValue;
        }

        return (left, right);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* best-effort temp cleanup */ }
    }
}
