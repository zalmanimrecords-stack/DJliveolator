using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Visuals;

/// <summary>
/// Probes video dimensions and duration with the FFmpeg <c>ffprobe</c> tool (CLI subprocess,
/// matching the audio decode decision). Video-only — images are handled by
/// <see cref="ImageHeaderProbe"/>; use <see cref="CompositeVisualMediaProbe"/> to route both.
/// </summary>
/// <remarks>
/// Requires <c>ffprobe</c> on PATH or an explicit path (constructor / <c>LIVEOLATOR_FFPROBE_PATH</c>).
/// A missing executable surfaces as <see cref="InvalidOperationException"/>; a non-zero exit or
/// unparseable output surfaces as <see cref="InvalidDataException"/> (→ a queryable Failed entry).
/// </remarks>
public sealed class FfprobeVideoProbe : IVisualMediaProbe
{
    public const string EnvironmentVariable = "LIVEOLATOR_FFPROBE_PATH";

    private readonly string _executablePath;

    public FfprobeVideoProbe(string? ffprobePath = null)
        => _executablePath = string.IsNullOrWhiteSpace(ffprobePath)
            ? (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } env ? env : "ffprobe")
            : ffprobePath;

    public async Task<VisualMediaInfo> ProbeAsync(
        string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (kind != VisualMediaKind.Video)
            throw new NotSupportedException(
                "FfprobeVideoProbe handles video only; use ImageHeaderProbe (or CompositeVisualMediaProbe) for images.");

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[]
                 {
                     "-v", "error",
                     "-select_streams", "v:0",
                     "-show_entries", "stream=width,height",
                     "-show_entries", "format=duration",
                     "-of", "json",
                     filePath,
                 })
            psi.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("ffprobe process failed to start.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not start ffprobe ('{_executablePath}'). Ensure FFmpeg/ffprobe is installed and on PATH " +
                "or configured via LIVEOLATOR_FFPROBE_PATH.", ex);
        }

        using (process)
        {
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            string json = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new InvalidDataException(
                    $"ffprobe failed to read '{filePath}' (exit {process.ExitCode}): {stderr.Trim()}");

            return Parse(json, filePath);
        }
    }

    private static VisualMediaInfo Parse(string json, string filePath)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            int width = 0, height = 0;
            if (root.TryGetProperty("streams", out JsonElement streams)
                && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
            {
                JsonElement stream = streams[0];
                if (stream.TryGetProperty("width", out JsonElement w)) width = w.GetInt32();
                if (stream.TryGetProperty("height", out JsonElement h)) height = h.GetInt32();
            }

            TimeSpan? duration = null;
            if (root.TryGetProperty("format", out JsonElement format)
                && format.TryGetProperty("duration", out JsonElement d)
                && d.ValueKind == JsonValueKind.String
                && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            return new VisualMediaInfo(width, height, duration);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not parse ffprobe output for '{filePath}'.", ex);
        }
    }
}
