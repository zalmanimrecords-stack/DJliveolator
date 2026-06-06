using System.Diagnostics;
using System.Text.Json;
using Liveolator.Core.Audio.Effects;

namespace Liveolator.Audio.Vst3;

public sealed record Vst3ScanRecord(
    string PluginUid,
    string Name,
    string Vendor,
    string ModulePath,
    int LatencySamples,
    IReadOnlyList<AudioEffectParameterDescriptor> Parameters);

/// <summary>
/// Runs the native VST3 scanner helper out of process. A crash, malformed result, or timeout is
/// quarantined in the catalog and never propagates into application startup.
/// </summary>
public sealed class Vst3ScannerClient : IAudioEffectPluginCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly string _scannerPath;
    private readonly string _catalogPath;
    private readonly IReadOnlyList<string> _additionalPaths;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _onWarning;
    private AudioEffectPluginDescriptor[] _plugins = Array.Empty<AudioEffectPluginDescriptor>();

    public Vst3ScannerClient(
        string scannerPath,
        string catalogPath,
        IEnumerable<string>? additionalPaths = null,
        TimeSpan? timeout = null,
        Action<string>? onWarning = null)
    {
        _scannerPath = scannerPath;
        _catalogPath = catalogPath;
        _additionalPaths = additionalPaths?.ToArray() ?? Array.Empty<string>();
        _timeout = timeout ?? DefaultTimeout;
        _onWarning = onWarning;
    }

    public IReadOnlyList<AudioEffectPluginDescriptor> Plugins => Volatile.Read(ref _plugins);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_scannerPath))
        {
            Volatile.Write(ref _plugins, await LoadCachedAsync(cancellationToken).ConfigureAwait(false));
            return;
        }

        string resultPath = _catalogPath + $".scan-{Guid.NewGuid():N}.json";
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _scannerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(resultPath);
            foreach (string path in StandardPaths().Concat(_additionalPaths).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add("--path");
                start.ArgumentList.Add(path);
            }

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("VST3 scanner process could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                _onWarning?.Invoke($"VST3 scanner timed out after {_timeout.TotalSeconds:0} seconds.");
                Volatile.Write(ref _plugins, QuarantineCached(await LoadCachedAsync(cancellationToken).ConfigureAwait(false)));
                return;
            }

            if (process.ExitCode != 0 || !File.Exists(resultPath))
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                _onWarning?.Invoke($"VST3 scanner failed with exit code {process.ExitCode}: {error}");
                Volatile.Write(ref _plugins, QuarantineCached(await LoadCachedAsync(cancellationToken).ConfigureAwait(false)));
                return;
            }

            Vst3ScanRecord[] records = await ReadRecordsAsync(resultPath, cancellationToken).ConfigureAwait(false);
            AudioEffectPluginDescriptor[] plugins = records
                .GroupBy(r => r.PluginUid, StringComparer.Ordinal)
                .Select(group => group.Count() == 1
                    ? ToDescriptor(group.Single())
                    : ToDescriptor(group.First()) with { IsAvailable = false, IsQuarantined = true })
                .ToArray();
            await SaveCatalogAsync(plugins, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _plugins, plugins);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onWarning?.Invoke($"VST3 scan was ignored ({ex.Message}).");
            Volatile.Write(ref _plugins, QuarantineCached(await LoadCachedAsync(cancellationToken).ConfigureAwait(false)));
        }
        finally
        {
            try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch (IOException) { }
        }
    }

    private async Task<AudioEffectPluginDescriptor[]> LoadCachedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_catalogPath))
            return Array.Empty<AudioEffectPluginDescriptor>();
        try
        {
            await using var stream = new FileStream(_catalogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<AudioEffectPluginDescriptor[]>(
                stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? Array.Empty<AudioEffectPluginDescriptor>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Cached VST3 catalog is unreadable ({ex.Message}).");
            return Array.Empty<AudioEffectPluginDescriptor>();
        }
    }

    private async Task SaveCatalogAsync(
        AudioEffectPluginDescriptor[] plugins,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        string temp = _catalogPath + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, plugins, cancellationToken: cancellationToken).ConfigureAwait(false);
        File.Move(temp, _catalogPath, overwrite: true);
    }

    private static async Task<Vst3ScanRecord[]> ReadRecordsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<Vst3ScanRecord[]>(
            stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<Vst3ScanRecord>();
    }

    private static AudioEffectPluginDescriptor ToDescriptor(Vst3ScanRecord record)
        => new(
            record.PluginUid,
            record.Name,
            record.Vendor,
            record.Parameters,
            record.LatencySamples,
            IsAvailable: true);

    private static AudioEffectPluginDescriptor[] QuarantineCached(
        IEnumerable<AudioEffectPluginDescriptor> plugins)
        => plugins.Select(p => p with { IsAvailable = false, IsQuarantined = true }).ToArray();

    private static IEnumerable<string> StandardPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Audio/Plug-Ins/VST3";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Audio/Plug-Ins/VST3");
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
