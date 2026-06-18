using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Liveolator.App.Features.Settings;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Renders the Settings view with the Diagnostics tab selected and captures it, so we can visually confirm
/// the Version / Build fields actually show in the running UI (not just that the binding compiles).
/// </summary>
public class SettingsDiagnosticsShot
{
    [AvaloniaFact]
    public void Capture_diagnostics_tab()
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);

        var vm = new SettingsViewModel(new FakeOutputCatalog(), new FakeCaptureCatalog(), new FakeMidi(), new FakeStore());
        var view = new SettingsView { DataContext = vm };
        var window = new Window { Content = view, Width = 700, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedItem = tabs.Items.OfType<TabItem>()
                .First(t => (t.Header as string) == "Diagnostics");
            Dispatcher.UIThread.RunJobs();

            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "settings-diagnostics.png"));

            // The entry assembly here is the test host, so the value is the host's version — assert only
            // that the bound properties resolve to non-empty text (the PNG confirms they render).
            Assert.False(string.IsNullOrWhiteSpace(vm.AppVersion));
            Assert.False(string.IsNullOrWhiteSpace(vm.BuildNumber));
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class FakeOutputCatalog : IAudioOutputDeviceCatalog
    {
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() => Array.Empty<AudioOutputDevice>();
    }

    private sealed class FakeCaptureCatalog : IAudioCaptureDeviceCatalog
    {
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() => Array.Empty<AudioCaptureDevice>();
    }

    private sealed class FakeMidi : IMidiDeviceProvider
    {
        public IReadOnlyList<string> GetInputDeviceNames() => Array.Empty<string>();
        public IReadOnlyList<string> GetOutputDeviceNames() => Array.Empty<string>();
        public IMidiInput? OpenInput(string deviceName) => null;
        public IMidiOutput? OpenOutput(string deviceName) => null;
    }

    private sealed class FakeStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(AppSettings.Default);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
