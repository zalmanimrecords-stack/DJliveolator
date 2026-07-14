using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Liveolator.Core.Platform;

namespace Liveolator.Platform.Audio;

/// <summary>
/// macOS <see cref="ISystemVolumeController"/> driving the OS output volume through <c>osascript</c>
/// (AppleScript): <c>set volume output volume N</c> where N is 0..100. This is the supported, sandbox-safe
/// way to move the system volume without a native CoreAudio binding. Reads use
/// <c>output volume of (get volume settings)</c>. Process failures are caught and reported as
/// "unavailable" rather than thrown.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacSystemVolumeController : ISystemVolumeController
{
    private readonly Action<string>? _onWarning;

    /// <param name="onWarning">Optional sink for diagnostic messages (wired to the app log at the root).</param>
    public MacSystemVolumeController(Action<string>? onWarning = null)
    {
        _onWarning = onWarning;
    }

    // The osascript tool is present on every macOS install; if reading the current volume succeeds we are
    // confident writes will too. Treat any failure as "no controllable volume on this host".
    public bool IsAvailable => TryReadVolume(out _);

    public double GetVolume() => TryReadVolume(out double volume) ? volume : 0.0;

    public void SetVolume(double level)
    {
        int percent = (int)Math.Round(Math.Clamp(level, 0.0, 1.0) * 100.0);
        string script = $"set volume output volume {percent.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            RunOsascript(script, out _);
        }
        catch (Exception ex)
        {
            Warn($"Failed to set output volume to {percent}%: {ex.Message}");
        }
    }

    private bool TryReadVolume(out double volume)
    {
        volume = 0.0;
        try
        {
            if (!RunOsascript("output volume of (get volume settings)", out string output))
                return false;
            if (!int.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
                return false;
            volume = Math.Clamp(percent / 100.0, 0.0, 1.0);
            return true;
        }
        catch (Exception ex)
        {
            Warn($"Failed to read output volume: {ex.Message}");
            return false;
        }
    }

    private static bool RunOsascript(string script, out string output)
    {
        output = string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process is null)
            return false;

        output = process.StandardOutput.ReadToEnd();
        // Bound the wait so a stuck helper never blocks a UI gesture; osascript returns near-instantly.
        if (!process.WaitForExit(2000))
        {
            try { process.Kill(); } catch { /* best-effort */ }
            return false;
        }
        return process.ExitCode == 0;
    }

    private void Warn(string message) => _onWarning?.Invoke($"MacSystemVolumeController: {message}");
}
