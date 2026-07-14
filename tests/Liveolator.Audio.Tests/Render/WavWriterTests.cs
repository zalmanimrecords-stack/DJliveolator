using System.Buffers.Binary;
using System.Text;
using Liveolator.Audio.Render;
using Xunit;

namespace Liveolator.Audio.Tests.Render;

public class WavWriterTests
{
    [Fact]
    public void WriteMono_ProducesReadableHeaderAndSamples()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-wav-{Guid.NewGuid():N}.wav");
        try
        {
            float[] samples = { 0f, 0.5f, -0.5f, 1f, -1f };
            WavWriter.WriteMono(path, samples, sampleRate: 44_100);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)));   // channels
            Assert.Equal(44_100, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4))); // sample rate
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)));   // bits

            int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            Assert.Equal(samples.Length * 2, dataBytes);

            // 0.5 → ~16383; full-scale 1 → 32767; -1 → -32767.
            short second = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + 2, 2));
            Assert.InRange(second, (short)16000, (short)16500);
            short fourth = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + 6, 2));
            Assert.Equal(short.MaxValue, fourth);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteStereo_ProducesTwoChannelHeaderAndInterleavedSamples()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-wav-{Guid.NewGuid():N}.wav");
        try
        {
            float[] left = { 0f, 1f, -0.5f };
            float[] right = { 0.5f, -1f, 0f };
            WavWriter.WriteStereo(path, left, right, sampleRate: 48_000);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)));    // channels
            Assert.Equal(48_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4))); // sample rate
            Assert.Equal(48_000 * 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28, 4))); // byte rate
            Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(32, 2)));    // block align (2ch*2B)
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)));   // bits

            int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            Assert.Equal(3 * 2 * 2, dataBytes);   // 3 frames * 2 channels * 2 bytes

            // Frame 1 is (L=1, R=-1): interleaved as L then R right after the header.
            short l1 = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + 4, 2));
            short r1 = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + 6, 2));
            Assert.Equal(short.MaxValue, l1);
            Assert.Equal((short)-short.MaxValue, r1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteStereo_MismatchedChannelLengths_Throws()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-wav-{Guid.NewGuid():N}.wav");
        try
        {
            Assert.Throws<ArgumentException>(() =>
                WavWriter.WriteStereo(path, new float[] { 0f, 1f }, new float[] { 0f }, sampleRate: 44_100));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
