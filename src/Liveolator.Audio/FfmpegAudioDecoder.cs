using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;

namespace Liveolator.Audio;

/// <summary>
/// FFmpeg-backed decoder for compressed formats (mp3/flac/m4a/aac/ogg/opus) via the FFmpeg
/// command-line tool: it pipes <c>-f f32le -ac 1 -ar {target}</c> from FFmpeg's stdout and
/// streams the resulting mono float PCM. Implements the <see cref="IAudioDecoder"/> seam (doc 16).
/// WAV is handled by the pure-managed <see cref="WavAudioDecoder"/>, so this decoder rejects it.
/// </summary>
/// <remarks>
/// Requires the FFmpeg executable to be installed and resolvable (PATH or an explicit path via
/// <see cref="FfmpegOptions"/>). A missing executable surfaces as a clear
/// <see cref="InvalidOperationException"/>; a non-zero FFmpeg exit surfaces as
/// <see cref="InvalidDataException"/> carrying FFmpeg's stderr.
/// </remarks>
public sealed class FfmpegAudioDecoder : IAudioDecoder
{
    // Compressed/container formats handled here. WAV is intentionally excluded — the pure-managed
    // WavAudioDecoder owns it so the common case needs no external dependency.
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus"
    };

    private readonly string _executablePath;

    public FfmpegAudioDecoder(string? executablePath = null)
        => _executablePath = string.IsNullOrWhiteSpace(executablePath) ? "ffmpeg" : executablePath;

    public bool CanDecode(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("f32le");   // 32-bit float, little-endian
        psi.ArgumentList.Add("-ac"); psi.ArgumentList.Add("1");      // mono downmix
        psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add(targetSampleRate.ToString());
        psi.ArgumentList.Add("pipe:1");

        Process process;
        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("FFmpeg process failed to start.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not start FFmpeg ('{_executablePath}'). Ensure FFmpeg is installed and on PATH " +
                "or configured via LIVEOLATOR_FFMPEG_PATH.", ex);
        }

        using (process)
        {
            // Drain stderr concurrently so a chatty FFmpeg can't deadlock the stdout pipe.
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            Stream stdout = process.StandardOutput.BaseStream;
            var buffer = new byte[16384];     // 4096 floats per full read
            int leftover = 0;
            int read;
            while ((read = await stdout.ReadAsync(buffer.AsMemory(leftover, buffer.Length - leftover), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                int available = leftover + read;
                int floatCount = available / 4;
                if (floatCount > 0)
                {
                    var samples = new float[floatCount];
                    for (int i = 0; i < floatCount; i++)
                        samples[i] = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(i * 4, 4));
                    yield return samples;
                }

                leftover = available - floatCount * 4;
                if (leftover > 0)
                    Array.Copy(buffer, floatCount * 4, buffer, 0, leftover); // carry a partial sample
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException(
                    $"FFmpeg failed to decode '{filePath}' (exit {process.ExitCode}): {stderr.Trim()}");
        }
    }
}
