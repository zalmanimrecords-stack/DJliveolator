using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// Cross-platform <see cref="ILogFileLocator"/>: opens the log folder in Explorer (Windows), Finder
/// (macOS), or the default file manager (Linux). Opening the shell is best-effort and never throws —
/// a failure is logged to <see cref="Trace"/>, matching the rule that diagnostics must not crash the app.
/// </summary>
public sealed class LogFileLocator : ILogFileLocator
{
    private readonly FileLoggerOptions _options;

    public LogFileLocator(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Directory => _options.Directory;

    public string CurrentFilePath => _options.CurrentFilePath;

    public void RevealInFileManager()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_options.Directory); // make sure there is something to open
            ProcessStartInfo start = OpenFolderCommand(_options.Directory);
            Process.Start(start);
        }
        catch (Exception ex) when (
            ex is System.ComponentModel.Win32Exception or InvalidOperationException
               or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not open the log folder '{_options.Directory}': {ex.Message}");
        }
    }

    private static ProcessStartInfo OpenFolderCommand(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new ProcessStartInfo("open", $"\"{directory}\"") { UseShellExecute = false };
        return new ProcessStartInfo("xdg-open", $"\"{directory}\"") { UseShellExecute = false };
    }
}
