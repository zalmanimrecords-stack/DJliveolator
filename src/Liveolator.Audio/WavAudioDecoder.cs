using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;

namespace Liveolator.Audio;

/// <summary>
/// Pure-managed WAV decoder (no native deps): RIFF/WAVE parsing for 16-bit, 24-bit PCM and
/// 32-bit IEEE-float, downmixed to mono and linearly resampled to the analysis sample rate.
/// Implements the <see cref="IAudioDecoder"/> seam (doc 16). FFmpeg covers compressed formats
/// (mp3/flac/m4a) in a later decoder; WAV needs no native dependency, so it ships first.
/// </summary>
public sealed class WavAudioDecoder : IAudioDecoder
{
    public bool CanDecode(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        (float[] mono, int sourceRate) = DecodeToMono(bytes);
        float[] output = sourceRate == targetSampleRate ? mono : Resample(mono, sourceRate, targetSampleRate);

        const int block = 8192;
        for (int offset = 0; offset < output.Length; offset += block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(block, output.Length - offset);
            yield return new ReadOnlyMemory<float>(output, offset, length);
            await Task.Yield();
        }
    }

    private static (float[] mono, int sampleRate) DecodeToMono(byte[] bytes)
    {
        var b = new ReadOnlySpan<byte>(bytes);
        if (b.Length < 12 || !Tag(b, 0, "RIFF") || !Tag(b, 8, "WAVE"))
            throw new InvalidDataException("Not a RIFF/WAVE file.");

        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        int dataOffset = -1, dataLength = 0;

        int pos = 12;
        while (pos + 8 <= b.Length)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > b.Length)
                size = b.Length - body; // tolerate a truncated final chunk

            if (Tag(b, pos, "fmt ") && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(body + 14, 2));
                if (format == 0xFFFE && size >= 40) // WAVE_FORMAT_EXTENSIBLE → real format in subformat GUID
                    format = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(body + 24, 2));
            }
            else if (Tag(b, pos, "data"))
            {
                dataOffset = body;
                dataLength = size;
            }

            pos = body + size + (size & 1); // chunks are word-aligned
        }

        if (dataOffset < 0 || channels < 1 || sampleRate < 1)
            throw new InvalidDataException("WAV missing fmt/data or has invalid header.");

        ReadOnlySpan<byte> data = b.Slice(dataOffset, dataLength);
        float[] mono = (format, bits) switch
        {
            (1, 16) => DecodePcm16(data, channels),
            (1, 24) => DecodePcm24(data, channels),
            (3, 32) => DecodeFloat32(data, channels),
            _ => throw new NotSupportedException($"Unsupported WAV format {format}, {bits}-bit.")
        };
        return (mono, sampleRate);
    }

    private static float[] DecodePcm16(ReadOnlySpan<byte> data, int channels)
    {
        int frameBytes = channels * 2;
        int frames = data.Length / frameBytes;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * frameBytes;
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += BinaryPrimitives.ReadInt16LittleEndian(data.Slice(baseIdx + c * 2, 2)) / 32768f;
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float[] DecodePcm24(ReadOnlySpan<byte> data, int channels)
    {
        int frameBytes = channels * 3;
        int frames = data.Length / frameBytes;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * frameBytes;
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int o = baseIdx + c * 3;
                int sample = data[o] | (data[o + 1] << 8) | (data[o + 2] << 16);
                if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000); // sign-extend
                sum += sample / 8388608f;
            }
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float[] DecodeFloat32(ReadOnlySpan<byte> data, int channels)
    {
        int frameBytes = channels * 4;
        int frames = data.Length / frameBytes;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * frameBytes;
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += BinaryPrimitives.ReadSingleLittleEndian(data.Slice(baseIdx + c * 4, 4));
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0) return input;
        double ratio = (double)targetRate / sourceRate;
        int outLength = Math.Max(1, (int)(input.Length * ratio));
        var output = new float[outLength];
        for (int i = 0; i < outLength; i++)
        {
            double srcPos = i / ratio;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double frac = srcPos - i0;
            output[i] = (float)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }

    private static bool Tag(ReadOnlySpan<byte> b, int offset, string tag)
        => offset + 4 <= b.Length
           && b[offset] == tag[0] && b[offset + 1] == tag[1]
           && b[offset + 2] == tag[2] && b[offset + 3] == tag[3];
}
