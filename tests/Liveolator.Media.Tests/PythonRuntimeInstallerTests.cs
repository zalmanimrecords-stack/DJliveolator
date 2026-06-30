using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

public class PythonRuntimeInstallerTests
{
    private static readonly PythonRuntimeSpec Spec = new(
        "https://example.test/python.tar.gz",
        "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
        "python.tar.gz");

    [Fact]
    public async Task Install_SuccessPath_DownloadsVerifiesExtractsAndPips_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        var ops = new FakeOps { DownloadOk = true, VerifyOk = true, ExtractOk = true, PipOk = true, ImportOk = true };
        var installer = new PythonRuntimeInstaller(runtime, ops, Spec);
        var progress = new SyncProgress();

        bool ok = await installer.InstallAsync(progress);

        Assert.True(ok);
        Assert.Equal(1, ops.DownloadCalls);
        Assert.Equal(1, ops.VerifyCalls);
        Assert.Equal(1, ops.ExtractCalls);
        Assert.Equal(1, ops.PipCalls);
        Assert.Equal(InstallPhase.Done, progress.Phases[^1]);
    }

    [Fact]
    public async Task Install_ChecksumMismatch_DoesNotExtractOrPip_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        var ops = new FakeOps { DownloadOk = true, VerifyOk = false };
        var installer = new PythonRuntimeInstaller(runtime, ops, Spec);
        var progress = new SyncProgress();

        bool ok = await installer.InstallAsync(progress);

        Assert.False(ok);
        Assert.Equal(1, ops.VerifyCalls);
        Assert.Equal(0, ops.ExtractCalls); // never extract an unverified download
        Assert.Equal(0, ops.PipCalls);
        Assert.Contains(InstallPhase.Failed, progress.Phases);
    }

    [Fact]
    public async Task Install_OfflineDownloadFails_ReturnsFalse_AndStopsEarly()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        var ops = new FakeOps { DownloadOk = false };
        var installer = new PythonRuntimeInstaller(runtime, ops, Spec);

        bool ok = await installer.InstallAsync();

        Assert.False(ok);
        Assert.Equal(1, ops.DownloadCalls);
        Assert.Equal(0, ops.VerifyCalls);
        Assert.Equal(0, ops.ExtractCalls);
    }

    [Fact]
    public async Task Install_OpsThrows_IsSwallowed_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        var ops = new FakeOps { DownloadOk = true, ThrowOnVerify = true };
        var installer = new PythonRuntimeInstaller(runtime, ops, Spec);

        bool ok = await installer.InstallAsync();

        Assert.False(ok); // a thrown op never propagates onto the analysis/UI path
    }

    [Fact]
    public async Task Install_UnsupportedPlatform_NullSpec_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        var installer = new PythonRuntimeInstaller(runtime, new FakeOps(), spec: null);

        Assert.False(await installer.InstallAsync());
    }

    [Fact]
    public async Task Install_AlreadyInstalled_NoOps_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var runtime = new PythonRuntime(Path.Combine(dir.Path, "python"));
        // Interpreter already on disk and librosa importable → IsInstalled is true.
        Directory.CreateDirectory(Path.GetDirectoryName(runtime.InterpreterPath)!);
        File.WriteAllText(runtime.InterpreterPath, "stub");
        var ops = new FakeOps { ImportOk = true };
        var installer = new PythonRuntimeInstaller(runtime, ops, Spec);

        bool ok = await installer.InstallAsync();

        Assert.True(ok);
        Assert.Equal(0, ops.DownloadCalls); // nothing downloaded when already present
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> — records phases inline (no Progress&lt;T&gt; post-back race).</summary>
    private sealed class SyncProgress : IProgress<InstallProgress>
    {
        public List<InstallPhase> Phases { get; } = new();
        public void Report(InstallProgress value) => Phases.Add(value.Phase);
    }

    /// <summary>Hits NO network/disk archive/process — every op is a controllable flag.</summary>
    private sealed class FakeOps : IPythonRuntimeOps
    {
        public bool DownloadOk { get; init; }
        public bool VerifyOk { get; init; }
        public bool ExtractOk { get; init; }
        public bool PipOk { get; init; }
        public bool ImportOk { get; set; }
        public bool ThrowOnVerify { get; init; }

        public int DownloadCalls;
        public int VerifyCalls;
        public int ExtractCalls;
        public int PipCalls;

        // Tracks the runtime's interpreter path so a successful extract makes IsAvailable true.
        private string? _interpreterPath;

        public Task<bool> DownloadAsync(string url, string destPath, CancellationToken ct)
        {
            DownloadCalls++;
            return Task.FromResult(DownloadOk);
        }

        public Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken ct)
        {
            VerifyCalls++;
            if (ThrowOnVerify)
                throw new InvalidOperationException("boom");
            return Task.FromResult(VerifyOk);
        }

        public Task<bool> ExtractAsync(string archivePath, string destDir, CancellationToken ct)
        {
            ExtractCalls++;
            if (ExtractOk)
            {
                // Materialize the interpreter where PythonRuntime expects it, so IsAvailable flips true.
                // install_only archives drop a 'python/' dir into destDir → destDir/python/{python.exe|bin/python3}.
                _interpreterPath = OperatingSystem.IsWindows()
                    ? Path.Combine(destDir, "python", "python.exe")
                    : Path.Combine(destDir, "python", "bin", "python3");
                Directory.CreateDirectory(Path.GetDirectoryName(_interpreterPath)!);
                File.WriteAllText(_interpreterPath, "stub");
                ImportOk = true; // post-extract, librosa imports (pip ran)
            }
            return Task.FromResult(ExtractOk);
        }

        public Task<bool> PipInstallAsync(string interpreterPath, string package, CancellationToken ct)
        {
            PipCalls++;
            return Task.FromResult(PipOk);
        }

        public Task<bool> CanImportAsync(string interpreterPath, string module, CancellationToken ct)
            => Task.FromResult(ImportOk && File.Exists(interpreterPath));
    }
}
