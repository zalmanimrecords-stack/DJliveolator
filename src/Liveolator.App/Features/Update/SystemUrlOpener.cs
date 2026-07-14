using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Liveolator.App.Features.Update;

/// <summary>
/// Cross-platform <see cref="IUrlOpener"/>: launches the OS default browser for a URL. Mirrors the
/// platform handling in <c>LogFileLocator</c> — Windows shell-executes the URL, macOS uses <c>open</c>,
/// Linux uses <c>xdg-open</c>. A launch failure is logged and swallowed so it can never crash the caller
/// (global standards #16/#26).
/// </summary>
public sealed class SystemUrlOpener : IUrlOpener
{
    private readonly ILogger<SystemUrlOpener> _logger;

    public SystemUrlOpener(ILogger<SystemUrlOpener> logger) => _logger = logger;

    public void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(StartInfoFor(url));
        }
        catch (Exception ex) when (
            ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Could not open the URL '{Url}' in a browser.", url);
        }
    }

    private static ProcessStartInfo StartInfoFor(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            // UseShellExecute lets the shell resolve the default browser for an http(s) URL.
            return new ProcessStartInfo(url) { UseShellExecute = true };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new ProcessStartInfo("open", url);
        return new ProcessStartInfo("xdg-open", url);
    }
}
