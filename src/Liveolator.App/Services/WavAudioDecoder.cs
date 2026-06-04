using System.IO;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;

namespace Liveolator.App.Services;

/// <summary>
/// Minimal, dependency-free WAV decoder implementing the <see cref="IAudioDecoder"/> seam:
/// parses PCM (8/16/24/32-bit) and IEEE-float WAV, down-mixes to mono, and linearly resamples
/// to the analysis sample rate. Other formats (mp3/flac/…) await the FFmpeg decoder behind the
/// same seam (doc 16); for them <see cref="CanDecode"/> returns false and they are skipped.
/// </summary>
public sealed class WavAudioDecoder : IAudioDecoder
{
    private const int BlockSize = 1 << 16;

    public bool CanDecode(string filePath)
        => !string.IsNullOrEmpty(filePath)
           && string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken ct)
    {
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        // Parse + decode off the calling thread; the analysis pipeline re-accumulates blocks anyway.
        float[] mono = await Task.Run(() => DecodeToMono(filePath, targetSampleRate, ct), ct).ConfigureAwait(false);

        for (int i = 0; i < mono.Length; i += BlockSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(BlockSize, mono.Length - i);
            yield return mono.AsMemory(i, len);
        }
    }

    private static float[] DecodeToMono(string path, int targetRate, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Not a RIFF/WAV file.");
        reader.ReadUInt32(); // RIFF chunk size (ignored)
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Not a WAVE file.");

        WavFormat? format = null;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            uint chunkSize = reader.ReadUInt32();

            if (chunkId == "fmt ")
            {
                format = ReadFormat(reader, chunkSize);
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes((int)Math.Min(chunkSize, (uint)(stream.Length - stream.Position)));
                break; // data is the payload; stop once we have it
            }
            else
            {
                stream.Seek(chunkSize, SeekOrigin.Current);
            }

            if ((chunkSize & 1) == 1) // chunks are word-aligned
                stream.Seek(1, SeekOrigin.Current);
        }

        if (format is null || data is null)
            throw new InvalidDataException("WAV missing fmt/data chunk.");

        ct.ThrowIfCancellationRequested();
        float[] mono = DownmixToMono(data, format.Value);
        return Resample(mono, format.Value.SampleRate, targetRate);
    }

    private readonly record struct WavFormat(int Channels, int SampleRate, int BitsPerSample, bool IsFloat);

    private static WavFormat ReadFormat(BinaryReader reader, uint chunkSize)
    {
        ushort audioFormat = reader.ReadUInt16();
        ushort channels = reader.ReadUInt16();
        uint sampleRate = reader.ReadUInt32();
        reader.ReadUInt32(); // byte rate
        reader.ReadUInt16(); // block align
        ushort bitsPerSample = reader.ReadUInt16();

        bool isFloat = audioFormat == 3;
        int consumed = 16;

        if (audioFormat == 0xFFFE && chunkSize >= 40) // WAVE_FORMAT_EXTENSIBLE
        {
            reader.ReadUInt16();          // cbSize
            reader.ReadUInt16();          // valid bits per sample
            reader.ReadUInt32();          // channel mask
            ushort subFormat = reader.ReadUInt16(); // first 2 bytes of the sub-format GUID
            reader.ReadBytes(14);         // rest of the GUID
            isFloat = subFormat == 3;
            consumed = 40;
        }

        if (chunkSize > consumed)
            reader.BaseStream.Seek(chunkSize - consumed, SeekOrigin.Current);

        if (channels < 1)
            throw new InvalidDataException("WAV reports zero channels.");

        return new WavFormat(channels, (int)sampleRate, bitsPerSample, isFloat);
    }

    private static float[] DownmixToMono(byte[] data, WavFormat fmt)
    {
        int bytesPerSample = fmt.BitsPerSample / 8;
        if (bytesPerSample == 0)
            throw new InvalidDataException("Unsupported bit depth.");

        int frameBytes = bytesPerSample * fmt.Channels;
        int frames = data.Length / frameBytes;
        var mono = new float[frames];

        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            int baseOffset = f * frameBytes;
            for (int c = 0; c < fmt.Channels; c++)
                sum += ReadSample(data, baseOffset + c * bytesPerSample, fmt);
            mono[f] = (float)(sum / fmt.Channels);
        }

        return mono;
    }

    private static float ReadSample(byte[] data, int offset, WavFormat fmt)
    {
        if (fmt.IsFloat)
            return fmt.BitsPerSample == 64
                ? (float)BitConverter.ToDouble(data, offset)
                : BitConverter.ToSingle(data, offset);

        return fmt.BitsPerSample switch
        {
            8 => (data[offset] - 128) / 128f,                                  // unsigned
            16 => BitConverter.ToInt16(data, offset) / 32768f,
            24 => Read24(data, offset) / 8388608f,
            32 => BitConverter.ToInt32(data, offset) / 2147483648f,
            _ => throw new InvalidDataException($"Unsupported PCM bit depth: {fmt.BitsPerSample}."),
        };
    }

    private static int Read24(byte[] data, int offset)
    {
        int sample = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        if ((sample & 0x800000) != 0) // sign-extend negative
            sample |= unchecked((int)0xFF000000);
        return sample;
    }

    private static float[] Resample(float[] source, int sourceRate, int targetRate)
    {
        if (source.Length == 0 || sourceRate == targetRate)
            return source;

        double ratio = (double)targetRate / sourceRate;
        int outLength = Math.Max(1, (int)(source.Length * ratio));
        var output = new float[outLength];

        for (int i = 0; i < outLength; i++)
        {
            double srcPos = i / ratio;
            int i0 = (int)srcPos;
            double frac = srcPos - i0;
            float a = source[Math.Min(i0, source.Length - 1)];
            float b = source[Math.Min(i0 + 1, source.Length - 1)];
            output[i] = (float)(a + (b - a) * frac);
        }

        return output;
    }
}
