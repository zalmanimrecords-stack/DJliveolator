using System;
using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// Installs the optional offline advanced-analysis runtime (the per-user Python + librosa that powers
/// <see cref="ISongStructureAnalyzer"/>, doc 32 §2.1 — download on demand). Core depends only on this
/// interface; the concrete implementation downloads/extracts a portable CPython and runs pip, and lives
/// in Liveolator.Media. Pure seam — unit-testable with a fake (no network).
/// </summary>
public interface IAdvancedAnalysisInstaller
{
    /// <summary>True when the runtime is already present and usable (interpreter resolves and librosa imports).</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Downloads, verifies, extracts the portable Python runtime and installs librosa. Returns <c>true</c>
    /// on success, <c>false</c> on any failure (offline, 404, checksum mismatch, pip failure) — never throws
    /// except on cancellation. Safe to call when already installed (no-ops to <c>true</c>).
    /// </summary>
    Task<bool> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
}
