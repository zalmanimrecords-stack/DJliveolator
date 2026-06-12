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
}
