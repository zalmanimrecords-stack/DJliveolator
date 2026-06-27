using System;
using System.IO;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Layout;
using Liveolator.App.Shell;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Renders the LIVE and DJ tabs at small-laptop, 1080p, and 4K window sizes so the responsive width caps
/// (Theme/ResponsiveLayout.axaml) and the size-class wiring can be eyeballed under artifacts/ui-shots/.
/// Also asserts the shell lands on the expected tier at each size.
/// </summary>
public class ResponsiveShots
{
    [AvaloniaTheory]
    [InlineData(1366, 768, LayoutSizeClass.Standard)]   // small laptop (still above the 1180 compact edge)
    [InlineData(1100, 720, LayoutSizeClass.Compact)]    // genuinely small / narrow window
    [InlineData(1920, 1080, LayoutSizeClass.Wide)]
    [InlineData(3840, 2160, LayoutSizeClass.Ultra)]
    public void Capture_live_and_dj_at(int width, int height, LayoutSizeClass expectedTier)
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots", "responsive");
        Directory.CreateDirectory(outDir);

        var devices = new HeadlessDeviceProvider();
        using var persistenceRoot = new Composition.TempPersistenceRoot();
        using var services = persistenceRoot.Build(devices, devices, devices, devices);
        var shell = services.GetRequiredService<MainWindowViewModel>();

        var window = new MainWindow { DataContext = shell, Width = width, Height = height };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedTier, window.CurrentSizeClass);

            foreach (var tab in shell.Tabs)
            {
                if (tab.Title is not ("LIVE" or "DJ"))
                    continue;
                shell.CurrentTab = tab;
                Dispatcher.UIThread.RunJobs();

                var frame = window.CaptureRenderedFrame();
                string name = $"{tab.Title}-{width}x{height}.png";
                frame?.Save(Path.Combine(outDir, name));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class HeadlessDeviceProvider :
        IMidiDeviceProvider,
        IAudioOutputDeviceCatalog,
        IAudioCaptureDeviceCatalog,
        IAudioCaptureSourceFactory
    {
        public IReadOnlyList<string> GetInputDeviceNames() => Array.Empty<string>();
        public IReadOnlyList<string> GetOutputDeviceNames() => Array.Empty<string>();
        public IMidiInput? OpenInput(string deviceName) => null;
        public IMidiOutput? OpenOutput(string deviceName) => null;
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() =>
            Array.Empty<AudioOutputDevice>();
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() =>
            Array.Empty<AudioCaptureDevice>();
        public IAudioSource CreateCaptureSource(AudioCaptureDevice device) =>
            throw new InvalidOperationException("Headless UI shots do not create capture sources.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
