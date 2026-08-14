using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Liveolator.Core.Analysis;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio;

/// <summary>
/// Measures integrated loudness with the FFmpeg CLI's <c>ebur128</c> filter — the same subprocess approach
/// as <see cref="FfmpegAudioDecoder"/>, so this adds no dependency.
/// <para>Deliberately not a C# BS.1770 implementation: FFmpeg already ships a correct, well-tested meter,
/// and the number is only ever consumed offline. A managed meter is worth writing when the value has to be
/// shown live or measured without FFmpeg present.</para>
/// </summary>
/// <remarks>
/// Unmeasurable input is a normal outcome, not a fault: a missing executable, an unreadable file, a
/// non-zero exit or a digitally silent track all return null so the caller leaves the clip at unity. Each
/// is logged, never swallowed silently.
/// </remarks>
public sealed class FfmpegLoudnessMeter : ILoudnessMeter
{
    // The summary block ends with the integrated figure; per-frame progress lines carry "I:" too, so the
    // LAST match is the one that matters (it equals the summary value).
    private static readonly Regex IntegratedLufs = new(
        @"I:\s*(-?\d+(?:\.\d+)?)\s*LUFS", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _executablePath;
    private readonly ILogger? _logger;

    public FfmpegLoudnessMeter(string? executablePath = null, ILogger? logger = null)
    {
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? FfmpegOptions.FromEnvironment().ExecutablePath
            : executablePath.Trim();
        _logger = logger;
    }

    public async Task<double?> MeasureIntegratedLufsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ebur128 reports on stderr; -f null discards the decoded audio so nothing is written to disk.
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-af"); psi.ArgumentList.Add("ebur128");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                _logger?.LogWarning("FFmpeg did not start; leaving {Path} unmeasured", path);
                return null;
            }

            // Read stderr to completion before waiting, or a large report can fill the pipe and deadlock.
            var stderr = new StringBuilder();
            Task readErr = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                    stderr.AppendLine(line);
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await readErr.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _logger?.LogWarning(
                    "FFmpeg exited {Code} measuring {Path}; leaving it unmeasured", process.ExitCode, path);
                return null;
            }

            return ParseIntegratedLufs(stderr.ToString());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // A missing or unusable FFmpeg must not fail a scan — the track simply stays at unity.
            _logger?.LogWarning(ex, "Could not measure loudness of {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// The integrated figure from an ebur128 report, or null when absent or non-finite. Digital silence
    /// reports <c>-inf</c>, which is unusable as a gain reference and so reads as "not measured".
    /// </summary>
    internal static double? ParseIntegratedLufs(string report)
    {
        if (string.IsNullOrEmpty(report))
            return null;

        MatchCollection matches = IntegratedLufs.Matches(report);
        if (matches.Count == 0)
            return null;

        string value = matches[^1].Groups[1].Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lufs)
            && double.IsFinite(lufs)
                ? lufs
                : null;
    }
}
