using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Media.Analysis;

/// <summary>
/// The side-effecting operations a <see cref="PythonRuntimeInstaller"/> performs, factored behind one
/// internal seam so the installer's orchestration unit-tests with a fake and hits NO network, disk
/// archive, or pip process. The default implementation (<see cref="RealPythonRuntimeOps"/>) does the
/// real HTTP download, SHA-256 verify, archive extraction, and pip invocation.
/// </summary>
internal interface IPythonRuntimeOps
{
    /// <summary>Download <paramref name="url"/> to <paramref name="destPath"/>. False on offline / 404 / IO error.</summary>
    Task<bool> DownloadAsync(string url, string destPath, CancellationToken ct);

    /// <summary>True when the file at <paramref name="path"/> hashes to <paramref name="expectedSha256"/> (hex, case-insensitive).</summary>
    Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken ct);

    /// <summary>Extract the archive at <paramref name="archivePath"/> into <paramref name="destDir"/>. False on any failure.</summary>
    Task<bool> ExtractAsync(string archivePath, string destDir, CancellationToken ct);

    /// <summary>Run <c>{interpreter} -m pip install {package}</c>. False on non-zero exit / launch failure.</summary>
    Task<bool> PipInstallAsync(string interpreterPath, string package, CancellationToken ct);

    /// <summary>True when <c>{interpreter} -c "import {module}"</c> exits 0 (the package is importable).</summary>
    Task<bool> CanImportAsync(string interpreterPath, string module, CancellationToken ct);
}
