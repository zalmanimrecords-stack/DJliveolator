using System.Diagnostics;
using Liveolator.Core.Enrichment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Online;

/// <summary>
/// <see cref="IAudioFingerprinter"/> backed by the Chromaprint <c>fpcalc</c> command-line tool (doc 16),
/// mirroring the FFmpeg-CLI pattern in the Audio binding. Runs <c>fpcalc -json &lt;file&gt;</c>, then
/// <see cref="FpcalcOutputParser"/> turns its stdout into an <see cref="AudioFingerprint"/>. The native
/// binary is required at runtime (fetched per-platform, like FFmpeg/BASS) — this type is verified
/// manually; the parsing it delegates to is unit-tested.
/// </summary>
/// <remarks>
/// Offline-first: a missing binary, an unreadable file, or a non-zero exit is logged and resolves to
/// <c>null</c> (never thrown), so a failed fingerprint cleanly falls back to a tag-based lookup.
/// </remarks>
public sealed class FpcalcFingerprinter : IAudioFingerprinter
{
    private readonly string _executablePath;
    private readonly ILogger _logger;

    public FpcalcFingerprinter(string? executablePath = null, ILogger<FpcalcFingerprinter>? logger = null)
    {
        _executablePath = string.IsNullOrWhiteSpace(executablePath) ? "fpcalc" : executablePath;
        _logger = logger ?? NullLogger<FpcalcFingerprinter>.Instance;
    }

    public async Task<AudioFingerprint?> ComputeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Fingerprint skipped: file not found at '{Path}'.", filePath);
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-json");
        psi.ArgumentList.Add(filePath);

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogWarning("fpcalc process failed to start ('{Exe}').", _executablePath);
                return null;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await stderrTask.ConfigureAwait(false);
                _logger.LogWarning("fpcalc exited {Code} for '{Path}': {Error}", process.ExitCode, filePath, stderr.Trim());
                return null;
            }

            return FpcalcOutputParser.Parse(stdout);
        }
        catch (Exception ex)
        {
            // Missing binary (Win32Exception/FileNotFound) or any process error → no fingerprint, no throw.
            _logger.LogWarning(ex,
                "Could not run fpcalc ('{Exe}') for '{Path}'. Ensure Chromaprint/fpcalc is installed and on PATH.",
                _executablePath, filePath);
            return null;
        }
    }
}
