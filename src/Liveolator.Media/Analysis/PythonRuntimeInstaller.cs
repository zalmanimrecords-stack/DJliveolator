using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Structure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Downloads a portable, redistributable CPython (python-build-standalone) into the per-user dir resolved
/// by <see cref="PythonRuntime"/>, verifies its published SHA-256 before extracting, then installs librosa
/// via pip — the "Enable advanced analysis" download (doc 32 §2.1). Reversible (everything lands under the
/// per-user python dir) and graceful: offline / 404 / checksum-mismatch / pip-failure all resolve to
/// <c>false</c> + a logged warning, never an exception (except on cancellation).
/// </summary>
public sealed class PythonRuntimeInstaller : IAdvancedAnalysisInstaller
{
    private const string LibrosaModule = "librosa";

    // Pip packages the one-click download provisions. librosa powers structure analysis (doc 32 Phase 1);
    // openunmix powers stem separation (Phase 2) and pulls torch + soundfile as its own dependencies.
    private static readonly string[] PipPackages = { "librosa", "openunmix", "soundfile" };

    private readonly PythonRuntime _runtime;
    private readonly IPythonRuntimeOps _ops;
    private readonly PythonRuntimeSpec? _spec;
    private readonly ILogger _logger;

    public PythonRuntimeInstaller(
        PythonRuntime? runtime = null,
        ILogger<PythonRuntimeInstaller>? logger = null)
        : this(runtime, ops: null, spec: PythonRuntimeSpec.ForCurrentPlatform(), logger)
    {
    }

    /// <summary>Test seam: inject a fake <see cref="IPythonRuntimeOps"/> and an explicit spec (no network).</summary>
    internal PythonRuntimeInstaller(
        PythonRuntime? runtime,
        IPythonRuntimeOps? ops,
        PythonRuntimeSpec? spec,
        ILogger<PythonRuntimeInstaller>? logger = null)
    {
        _runtime = runtime ?? new PythonRuntime();
        _ops = ops ?? new RealPythonRuntimeOps();
        _spec = spec;
        _logger = logger ?? NullLogger<PythonRuntimeInstaller>.Instance;
    }

    /// <summary>Installed = the interpreter is on disk AND librosa imports under it.</summary>
    public bool IsInstalled
    {
        get
        {
            if (!_runtime.IsAvailable)
                return false;
            // CanImport launches the interpreter; block briefly — this is an idle-time/UI-thread-off check.
            return _ops.CanImportAsync(_runtime.InterpreterPath, LibrosaModule, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }

    public async Task<bool> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (IsInstalled)
            {
                Report(progress, InstallPhase.Done, 1.0, "Advanced analysis already installed.");
                return true;
            }

            if (_spec is null)
            {
                _logger.LogWarning("Advanced-analysis install unsupported on this OS/architecture.");
                Report(progress, InstallPhase.Failed, 0, "Unsupported platform.");
                return false;
            }

            Directory.CreateDirectory(_runtime.BaseDir);
            string archivePath = Path.Combine(_runtime.BaseDir, _spec.ArchiveFileName);

            Report(progress, InstallPhase.Downloading, 0.0, "Downloading Python runtime…");
            if (!await _ops.DownloadAsync(_spec.Url, archivePath, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Advanced-analysis install failed: could not download '{Url}'.", _spec.Url);
                Report(progress, InstallPhase.Failed, 0, "Download failed (offline or unavailable).");
                return false;
            }

            Report(progress, InstallPhase.Verifying, 0.5, "Verifying download…");
            if (!await _ops.VerifySha256Async(archivePath, _spec.Sha256, ct).ConfigureAwait(false))
            {
                // Security: never extract an unverified download.
                _logger.LogWarning("Advanced-analysis install failed: SHA-256 mismatch for '{File}'.", archivePath);
                TryDelete(archivePath);
                Report(progress, InstallPhase.Failed, 0, "Checksum verification failed.");
                return false;
            }

            Report(progress, InstallPhase.Extracting, 0.6, "Extracting Python runtime…");
            // install_only archives contain a top-level 'python/' dir, so extracting into BaseDir's parent
            // yields '<BaseDir>/python.exe' (or 'bin/python3') — exactly PythonRuntime.InterpreterPath.
            string extractInto = Path.GetDirectoryName(_runtime.BaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                 ?? _runtime.BaseDir;
            if (!await _ops.ExtractAsync(archivePath, extractInto, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Advanced-analysis install failed: could not extract '{File}'.", archivePath);
                Report(progress, InstallPhase.Failed, 0, "Extraction failed.");
                return false;
            }
            TryDelete(archivePath);

            Report(progress, InstallPhase.InstallingPackages, 0.8, "Installing analysis packages…");
            foreach (string package in PipPackages)
            {
                if (!await _ops.PipInstallAsync(_runtime.InterpreterPath, package, ct).ConfigureAwait(false))
                {
                    _logger.LogWarning("Advanced-analysis install failed: pip install {Package} failed.", package);
                    Report(progress, InstallPhase.Failed, 0, $"Installing {package} failed.");
                    return false;
                }
            }

            // Confirm the interpreter resolves AND librosa actually imports before declaring success.
            if (!IsInstalled)
            {
                _logger.LogWarning("Advanced-analysis install failed: librosa did not import after install.");
                Report(progress, InstallPhase.Failed, 0, "Verification after install failed.");
                return false;
            }

            Report(progress, InstallPhase.Done, 1.0, "Advanced analysis installed.");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any unexpected IO/process failure → false + log, never thrown onto the caller.
            _logger.LogWarning(ex, "Advanced-analysis install failed unexpectedly.");
            Report(progress, InstallPhase.Failed, 0, "Install failed.");
            return false;
        }
    }

    private static void Report(IProgress<InstallProgress>? progress, InstallPhase phase, double fraction, string message)
        => progress?.Report(new InstallProgress(phase, fraction, message));

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException ex) { _logger.LogDebug(ex, "Could not delete temp archive '{Path}'.", path); }
        catch (UnauthorizedAccessException ex) { _logger.LogDebug(ex, "Could not delete temp archive '{Path}'.", path); }
    }
}
