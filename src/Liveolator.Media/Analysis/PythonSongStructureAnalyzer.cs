using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Structure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Media.Analysis;

/// <summary>
/// <see cref="ISongStructureAnalyzer"/> backed by the <c>analyze_structure.py</c> librosa script run as a
/// subprocess (doc 32), mirroring the FFmpeg/fpcalc CLI pattern. The interpreter is resolved by
/// <see cref="PythonRuntime"/> (download-on-demand, §2.1); the script reads the audio file and writes the
/// section JSON to stdout. Offline / import-time only — never the realtime path.
/// </summary>
/// <remarks>
/// Graceful by contract: a missing runtime/script, a non-zero exit, or unparsable output is logged once and
/// resolves to <c>null</c> (never thrown), so advanced analysis degrades cleanly when Python is absent.
/// </remarks>
public sealed class PythonSongStructureAnalyzer : ISongStructureAnalyzer
{
    private readonly PythonRuntime _runtime;
    private readonly string _scriptPath;
    private readonly ILogger _logger;
    private int _absenceLogged;

    public PythonSongStructureAnalyzer(
        PythonRuntime? runtime = null,
        string? scriptPath = null,
        ILogger<PythonSongStructureAnalyzer>? logger = null)
    {
        _runtime = runtime ?? new PythonRuntime();
        _scriptPath = string.IsNullOrWhiteSpace(scriptPath) ? DefaultScriptPath() : scriptPath;
        _logger = logger ?? NullLogger<PythonSongStructureAnalyzer>.Instance;
    }

    public async Task<SongStructure?> AnalyzeAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken = default)
    {
        // decoder is part of the locked seam contract; librosa reads the file directly so it is unused here.
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Structure analysis skipped: file not found at '{Path}'.", filePath);
            return null;
        }

        if (!_runtime.IsAvailable || !File.Exists(_scriptPath))
        {
            // No-op until the runtime is downloaded (decision §2.1). Log only once to avoid spamming a scan.
            if (Interlocked.Exchange(ref _absenceLogged, 1) == 0)
                _logger.LogInformation(
                    "Structure analysis unavailable: Python runtime ('{Py}') or script ('{Script}') not present. " +
                    "Enable advanced analysis to download it.", _runtime.InterpreterPath, _scriptPath);
            return null;
        }

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

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogWarning("Python process failed to start ('{Py}').", _runtime.InterpreterPath);
                return null;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await stderrTask.ConfigureAwait(false);
                _logger.LogWarning("Structure analysis exited {Code} for '{Path}': {Error}",
                    process.ExitCode, filePath, stderr.Trim());
                return null;
            }

            SongStructure? result = StructureOutputParser.Parse(stdout);
            if (result is null)
                _logger.LogWarning("Structure analysis produced no parsable sections for '{Path}'.", filePath);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing binary, IO error, or any process failure → no structure, no throw.
            _logger.LogWarning(ex, "Could not run structure analysis ('{Py}') for '{Path}'.",
                _runtime.InterpreterPath, filePath);
            return null;
        }
    }

    private static string DefaultScriptPath() =>
        Path.Combine(AppContext.BaseDirectory, "scripts", "analyze_structure.py");
}
