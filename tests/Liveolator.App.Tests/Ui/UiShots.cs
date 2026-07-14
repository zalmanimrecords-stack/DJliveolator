using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Composition;
using Liveolator.App.Shell;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

[assembly: AvaloniaTestApplication(typeof(Liveolator.App.Tests.Ui.UiShotAppBuilder))]

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Headless render harness: boots the real App (styles, tokens, ViewLocator) and the real composed
/// view-models, then captures each shell tab to a PNG under artifacts/ui-shots/. This is the visual
/// parity loop — it lets us (and reviewers) see what the Avalonia app actually renders and compare it
/// to design/mockups/, instead of building blind.
/// </summary>
public static class UiShotAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<global::Liveolator.App.App>()
            .UseSkia() // real rasterizer + font manager so CaptureRenderedFrame produces pixels
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

public class UiShots
{
    [AvaloniaFact]
    public void Capture_every_tab_to_png()
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);

        var devices = new HeadlessDeviceProvider();
        using var persistenceRoot = new Composition.TempPersistenceRoot();
        using var services = persistenceRoot.Build(devices, devices, devices, devices);
        var shell = services.GetRequiredService<MainWindowViewModel>();

        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            int index = 0;
            foreach (var tab in shell.Tabs)
            {
                shell.CurrentTab = tab;
                Dispatcher.UIThread.RunJobs();

                var frame = window.CaptureRenderedFrame();
                string name = $"{index:00}-{Safe(tab.Title)}.png";
                frame?.Save(Path.Combine(outDir, name));
                index++;
            }

            Assert.True(index > 0);
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

    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
