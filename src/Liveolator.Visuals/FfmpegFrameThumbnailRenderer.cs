using System.Diagnostics;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Visuals;

/// <summary>
/// Renders a preview thumbnail for a <see cref="VisualMediaKind.Video"/> asset by extracting a single
/// frame with the FFmpeg <c>ffmpeg</c> tool (CLI subprocess, matching the ffprobe decode decision) and
/// decoding that frame through the shared <see cref="SkiaThumbnail"/> path. Image is not handled here —
/// use <see cref="CompositeVisualThumbnailRenderer"/> to route by kind.
/// </summary>
/// <remarks>
/// Requires <c>ffmpeg</c> on PATH or an explicit path (constructor / <c>LIVEOLATOR_FFMPEG_PATH</c>). A
/// missing tool or a failed extraction returns <c>null</c> (the preview is a convenience, not a critical
/// flow) with a logged warning — the library tab falls back to a placeholder.
/// </remarks>
public sealed class FfmpegFrameThumbnailRenderer : IVisualThumbnailRenderer
{
    public const string EnvironmentVariable = "LIVEOLATOR_FFMPEG_PATH";

    private readonly string _executablePath;
    private readonly ILogger<FfmpegFrameThumbnailRenderer> _logger;

    public FfmpegFrameThumbnailRenderer(string? ffmpegPath = null, ILogger<FfmpegFrameThumbnailRenderer>? logger = null)
    {
        _executablePath = string.IsNullOrWhiteSpace(ffmpegPath)
            ? (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } env ? env : "ffmpeg")
            : ffmpegPath;
        _logger = logger ?? NullLogger<FfmpegFrameThumbnailRenderer>.Instance;
    }

    public async Task<VisualPreviewFrame?> RenderAsync(
        string filePath, VisualMediaKind kind, int maxEdge, CancellationToken cancellationToken = default)
    {
        if (kind != VisualMediaKind.Video || string.IsNullOrWhiteSpace(filePath))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Decode the first frame and emit it as a single PNG on stdout; Skia then decodes + scales it,
        // so the output dimensions are discovered from the frame (no need to know them up front).
        foreach (string arg in new[]
                 {
                     "-v", "error",
                     "-i", filePath,
                     "-frames:v", "1",
                     "-f", "image2pipe",
                     "-vcodec", "png",
                     "pipe:1",
                 })
            psi.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg process failed to start.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not start ffmpeg ('{Executable}') for a video preview. Install FFmpeg or set {EnvVar}.",
                _executablePath, EnvironmentVariable);
            return null;
        }

        try
        {
            using (process)
            {
                using var frameData = new MemoryStream();
                // Drain stdout (the PNG) and stderr concurrently before waiting, so a large frame can
                // never deadlock by filling a pipe buffer the process is blocked writing to.
                Task copyOut = process.StandardOutput.BaseStream.CopyToAsync(frameData, cancellationToken);
                Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await Task.WhenAll(copyOut, stderrTask).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0 || frameData.Length == 0)
                {
                    _logger.LogWarning(
                        "ffmpeg could not extract a preview frame from '{FilePath}' (exit {ExitCode}): {Error}",
                        filePath, process.ExitCode, (await stderrTask.ConfigureAwait(false)).Trim());
                    return null;
                }

                frameData.Position = 0;
                return SkiaThumbnail.DecodeScaled(frameData, maxEdge);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not render video preview for {FilePath}.", filePath);
            return null;
        }
    }
}
