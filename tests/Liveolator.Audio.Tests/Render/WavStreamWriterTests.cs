using System.Buffers.Binary;
using System.Text;
using Liveolator.Audio.Render;
using Xunit;

namespace Liveolator.Audio.Tests.Render;

public class WavStreamWriterTests
{
    [Fact]
    public void Streaming_WritesValidHeader_AndPatchesSizesOnDispose()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-stream-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WavStreamWriter(path, channels: 2, sampleRate: 48_000))
            {
                writer.Write(new float[] { 0f, 0.5f });   // one stereo frame
                writer.Write(new float[] { 1f, -1f });     // another stereo frame
            }

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)));    // channels
            Assert.Equal(48_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4))); // rate
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)));   // bits

            int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            Assert.Equal(4 * 2, dataBytes);                 // 4 samples * 2 bytes
            int riffSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
            Assert.Equal(36 + dataBytes, riffSize);

            short third = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + 4, 2)); // the 1f sample
            Assert.Equal(short.MaxValue, third);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EmptyRecording_ProducesZeroDataBytes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-stream-{Guid.NewGuid():N}.wav");
        try
        {
            using (new WavStreamWriter(path, channels: 2, sampleRate: 44_100)) { }

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(44, bytes.Length);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
