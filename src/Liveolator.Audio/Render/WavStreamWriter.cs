using System.Buffers.Binary;

namespace Liveolator.Audio.Render;

/// <summary>
/// Streams interleaved float PCM to a 16-bit little-endian WAV file incrementally, so a long live master
/// recording (roadmap X2) never buffers the whole session in memory. The RIFF/data sizes are written as
/// placeholders up front and patched on <see cref="Dispose"/>; samples are clamped to [-1, 1] before
/// quantizing, matching <see cref="WavWriter"/>'s format (the offline render output) so recordings and
/// renders are interchangeable. Not thread-safe: the owner serializes <see cref="Write"/> calls.
/// </summary>
public sealed class WavStreamWriter : IDisposable
{
    private const int BitsPerSample = 16;
    private const short PcmFormat = 1;
    private const int HeaderBytes = 44;

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private int _dataBytes;
    private bool _disposed;

    public WavStreamWriter(string path, int channels, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        Channels = channels;
        SampleRate = sampleRate;

        int bytesPerSample = BitsPerSample / 8;
        int blockAlign = channels * bytesPerSample;
        int byteRate = sampleRate * blockAlign;

        _stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        _writer = new BinaryWriter(_stream);

        // RIFF / WAVE header with placeholder sizes (patched on Dispose once the total is known).
        _writer.Write("RIFF"u8);
        _writer.Write(0);                  // chunk size placeholder
        _writer.Write("WAVE"u8);
        _writer.Write("fmt "u8);
        _writer.Write(16);                 // PCM fmt chunk size
        _writer.Write(PcmFormat);
        _writer.Write((short)channels);
        _writer.Write(sampleRate);
        _writer.Write(byteRate);
        _writer.Write((short)blockAlign);
        _writer.Write((short)BitsPerSample);
        _writer.Write("data"u8);
        _writer.Write(0);                  // data size placeholder
    }

    public int Channels { get; }

    public int SampleRate { get; }

    /// <summary>Total PCM data bytes written so far (excludes the 44-byte header).</summary>
    public int DataBytes => _dataBytes;

    /// <summary>Append a block of interleaved float samples (-1..1), quantized to 16-bit PCM.</summary>
    public void Write(ReadOnlySpan<float> interleaved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleaved.IsEmpty)
            return;

        Span<byte> pair = stackalloc byte[2];
        foreach (float sample in interleaved)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pair, ToPcm16(sample));
            _writer.Write(pair);
        }
        _dataBytes += interleaved.Length * (BitsPerSample / 8);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            // Patch the two size fields now that the total is known, then flush + close.
            _writer.Flush();
            _stream.Seek(4, SeekOrigin.Begin);
            _writer.Write(36 + _dataBytes);          // RIFF chunk size
            _stream.Seek(HeaderBytes - 4, SeekOrigin.Begin);
            _writer.Write(_dataBytes);               // data subchunk size
            _writer.Flush();
        }
        finally
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    private static short ToPcm16(float sample)
    {
        double clamped = Math.Clamp(sample, -1.0, 1.0);
        return (short)Math.Round(clamped * short.MaxValue);
    }
}
