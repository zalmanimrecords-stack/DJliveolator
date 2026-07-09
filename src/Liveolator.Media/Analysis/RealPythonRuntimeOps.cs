using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Real side effects for <see cref="PythonRuntimeInstaller"/>: HTTP download, SHA-256 verify, .tar.gz
/// extraction, and pip / import via the resolved interpreter. Each operation is self-contained and returns
/// a bool (no throw on the expected failure modes) so the installer's orchestration stays simple.
/// </summary>
internal sealed class RealPythonRuntimeOps : IPythonRuntimeOps
{
    // One shared client; the installer is short-lived but a static avoids socket exhaustion if reused.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<bool> DownloadAsync(string url, string destPath, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            string actual = Convert.ToHexString(hash);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public async Task<bool> ExtractAsync(string archivePath, string destDir, CancellationToken ct)
    {
        // Decompress the .gz to a temporary, SEEKABLE .tar first, then extract from that file. Extracting
        // straight from the non-seekable GZipStream corrupts entry names (observed: "python.exe" landing as
        // "python.exe_hon.exe"), which leaves the interpreter absent at its expected path and fails the
        // whole install. TarFile needs a seekable stream to read entry headers reliably.
        string tempTar = Path.Combine(destDir, Path.GetRandomFileName() + ".tar");
        try
        {
            Directory.CreateDirectory(destDir);

            await using (var gzFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var gzip = new GZipStream(gzFile, CompressionMode.Decompress))
            await using (var tarOut = new FileStream(tempTar, FileMode.Create, FileAccess.Write, FileShare.None))
                await gzip.CopyToAsync(tarOut, ct).ConfigureAwait(false);

            await using (var tarIn = new FileStream(tempTar, FileMode.Open, FileAccess.Read, FileShare.Read))
                await TarFile.ExtractToDirectoryAsync(tarIn, destDir, overwriteFiles: true, ct).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(tempTar)) File.Delete(tempTar); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public Task<bool> PipInstallAsync(string interpreterPath, string package, CancellationToken ct)
        => RunAsync(interpreterPath, ct, "-m", "pip", "install", package);

    public Task<bool> CanImportAsync(string interpreterPath, string module, CancellationToken ct)
    {
        if (!File.Exists(interpreterPath))
            return Task.FromResult(false);
        return RunAsync(interpreterPath, ct, "-c", "import " + module);
    }

    private static async Task<bool> RunAsync(string fileName, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
                return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
