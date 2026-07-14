using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Stems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Media.Analysis;

/// <summary>
/// <see cref="IStemSeparator"/> backed by the <c>separate_stems.py</c> Open-Unmix script run as a
/// subprocess (doc 32 Phase 2), mirroring <see cref="PythonSongStructureAnalyzer"/>. The interpreter is
/// resolved by <see cref="PythonRuntime"/> (download-on-demand, §2.1); the script writes four FLAC stems
/// into a local <see cref="StemStore"/> folder and prints a manifest to stdout. Offline / import-time
/// only — never the realtime path. A cache hit (stems already separated) skips the subprocess entirely.
/// </summary>
/// <remarks>
/// Graceful by contract: a missing runtime/script, a non-zero exit, or unparsable output is logged once
/// and resolves to <c>null</c> (never thrown), so stem separation degrades cleanly when Python is absent.
/// </remarks>
public sealed class OpenUnmixStemSeparator : IStemSeparator
{
    private readonly PythonRuntime _runtime;
    private readonly StemStore _store;
    private readonly string _scriptPath;
    private readonly ILogger _logger;
    private int _absenceLogged;

    public OpenUnmixStemSeparator(
        PythonRuntime? runtime = null,
        StemStore? store = null,
        string? scriptPath = null,
        ILogger<OpenUnmixStemSeparator>? logger = null)
    {
        _runtime = runtime ?? new PythonRuntime();
        _store = store ?? new StemStore();
        _scriptPath = string.IsNullOrWhiteSpace(scriptPath) ? DefaultScriptPath() : scriptPath;
        _logger = logger ?? NullLogger<OpenUnmixStemSeparator>.Instance;
    }

    public async Task<StemSet?> SeparateAsync(
        IAudioDecoder decoder, string filePath, CancellationToken ct = default)
    {
        // decoder is part of the locked seam contract; Open-Unmix reads the file directly so it is unused.
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Stem separation skipped: file not found at '{Path}'.", filePath);
            return null;
        }

        StemSet? cached = _store.TryLoad(filePath);
        if (cached is not null)
            return cached;

        if (!_runtime.IsAvailable || !File.Exists(_scriptPath))
        {
            // No-op until the runtime is downloaded (decision §2.1). Log only once to avoid spamming a scan.
            if (Interlocked.Exchange(ref _absenceLogged, 1) == 0)
                _logger.LogInformation(
                    "Stem separation unavailable: Python runtime ('{Py}') or script ('{Script}') not present. " +
                    "Enable advanced analysis to download it.", _runtime.InterpreterPath, _scriptPath);
            return null;
        }

        string outputDir = _store.FolderFor(filePath);

        var psi = new ProcessStartInfo
        {
            FileName = _runtime.InterpreterPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(_scriptPath);
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add(outputDir);

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogWarning("Python process failed to start ('{Py}').", _runtime.InterpreterPath);
                return null;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await stderrTask.ConfigureAwait(false);
                _logger.LogWarning("Stem separation exited {Code} for '{Path}': {Error}",
                    process.ExitCode, filePath, stderr.Trim());
                return null;
            }

            StemSet? result = StemManifestParser.Parse(stdout, filePath);
            if (result is null)
            {
                _logger.LogWarning("Stem separation produced no parsable manifest for '{Path}'.", filePath);
                return null;
            }

            _store.Save(result); // persist the manifest so the next load is a cache hit
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing binary, IO error, or any process failure -> no stems, no throw.
            _logger.LogWarning(ex, "Could not run stem separation ('{Py}') for '{Path}'.",
                _runtime.InterpreterPath, filePath);
            return null;
        }
    }

    private static string DefaultScriptPath() =>
        Path.Combine(AppContext.BaseDirectory, "scripts", "separate_stems.py");
}
