using System.Diagnostics;
using System.Text.Json;
using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

public sealed class ProcessVisualShaderProbe : IVisualShaderProbe
{
    private readonly string _helperPath;
    private readonly TimeSpan _timeout;

    public ProcessVisualShaderProbe(string helperPath, TimeSpan? timeout = null)
    {
        _helperPath = helperPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<VisualShaderProbeResult> ProbeAsync(
        string shaderPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_helperPath))
            return new(false, "The isolated shader probe helper is not installed.", Array.Empty<string>());

        string resultPath = Path.GetTempFileName();
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _helperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--shader");
            start.ArgumentList.Add(shaderPath);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(resultPath);

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Shader probe could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new(false, "Shader probe timed out.", Array.Empty<string>());
            }

            if (process.ExitCode != 0)
                return new(false, "Shader probe process failed.", Array.Empty<string>());
            await using var stream = new FileStream(resultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<VisualShaderProbeResult>(
                stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? new(false, "Shader probe returned no result.", Array.Empty<string>());
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return new(false, ex.Message, Array.Empty<string>());
        }
        finally
        {
            try { File.Delete(resultPath); } catch (IOException) { }
        }
    }
}
