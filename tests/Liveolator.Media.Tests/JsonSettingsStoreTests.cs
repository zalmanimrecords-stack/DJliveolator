using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Audio;
using Liveolator.Core.Settings;
using Liveolator.Media;
using Xunit;

namespace Liveolator.Media.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _root;

    public JsonSettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liveolator-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    private JsonSettingsStore NewStore() => new(_root);

    [Fact]
    public async Task Load_WhenNoFile_ReturnsDefaults()
    {
        AppSettings settings = await NewStore().LoadAsync();

        Assert.Equal(AppSettings.Default, settings);
    }

    [Fact]
    public void SaveThenLoad_DoesNotDeadlock_WhenBlockedUnderASynchronizationContext()
    {
        var store = NewStore();
        using var done = new ManualResetEventSlim(false);
        Exception? failure = null;

        // Reproduces window-close (SaveWindowLayout): a thread that OWNS a SynchronizationContext blocks
        // on the async settings IO with GetResult(). If the store omits ConfigureAwait(false) on its
        // stream dispose, that continuation is captured back onto this (blocked) context and never runs,
        // so GetResult never returns — the app freezes on X. The fix keeps the IO off the captured context.
        var thread = new Thread(() =>
        {
            SynchronizationContext? prev = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new BlockedContext());
            try
            {
                store.SaveAsync(AppSettings.Default).GetAwaiter().GetResult();
                _ = store.LoadAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { failure = ex; }
            finally { SynchronizationContext.SetSynchronizationContext(prev); done.Set(); }
        }) { IsBackground = true };
        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)),
            "settings save/load deadlocked when blocked under a SynchronizationContext (window-close freeze)");
        Assert.Null(failure);
    }

    // A SynchronizationContext whose posted continuations never run — exactly the state of the UI thread
    // while it is blocked in GetResult(). Send still runs inline so non-deadlocking paths behave normally.
    private sealed class BlockedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { /* never drained: blocked UI thread */ }
        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsSelections()
    {
        var store = NewStore();
        var settings = new AppSettings
        {
            Audio = new AudioSettings { OutputDeviceId = "bass:3", BufferMilliseconds = 25 },
            Midi = new MidiSettings { ControllerInputName = "Ableton Push", FeedbackOutputName = "Push" },
            Extensions = new ExtensionSettings
            {
                DeveloperMode = true,
                ActiveUiThemeId = "com.example.theme/night",
                ActiveKnobSkinId = "liveolator.control-skins/cobalt-knob",
                ActiveSliderSkinId = "liveolator.control-skins/amber-slider",
            },
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("bass:3", loaded.Audio.OutputDeviceId);
        Assert.Equal(25, loaded.Audio.BufferMilliseconds);
        Assert.Equal("Ableton Push", loaded.Midi.ControllerInputName);
        Assert.Equal("Push", loaded.Midi.FeedbackOutputName);
        Assert.True(loaded.Extensions.DeveloperMode);
        Assert.Equal("com.example.theme/night", loaded.Extensions.ActiveUiThemeId);
        Assert.Equal("liveolator.control-skins/cobalt-knob", loaded.Extensions.ActiveKnobSkinId);
        Assert.Equal("liveolator.control-skins/amber-slider", loaded.Extensions.ActiveSliderSkinId);
    }

    [Fact]
    public async Task Save_NormalizesBeforeWriting()
    {
        var store = NewStore();
        var settings = AppSettings.Default with
        {
            Audio = new AudioSettings { BufferMilliseconds = 9_999 },
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(AudioSettings.MaxBufferMs, loaded.Audio.BufferMilliseconds);
    }

    [Fact]
    public async Task Save_IsAtomic_NoTempLeftBehind()
    {
        var store = NewStore();

        await store.SaveAsync(AppSettings.Default);

        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsDefaultsWithWarning()
    {
        string warning = string.Empty;
        var store = new JsonSettingsStore(_root, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.FilePath, "{ this is not valid json");

        AppSettings settings = await store.LoadAsync();

        Assert.Equal(AppSettings.Default, settings);
        Assert.Contains("unreadable", warning);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsCaptureSourceSelection()
    {
        var store = NewStore();
        var settings = AppSettings.Default with
        {
            Audio = new AudioSettings
            {
                CaptureDeviceId = "2",
                CaptureSource = CaptureSourceKind.SystemLoopback,
            },
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("2", loaded.Audio.CaptureDeviceId);
        Assert.Equal(CaptureSourceKind.SystemLoopback, loaded.Audio.CaptureSource);
    }

    [Fact]
    public async Task Load_OlderFileWithoutCaptureFields_StillLoads()
    {
        // Backward compatibility: a version-1 file written before the capture fields existed must
        // load (missing capture fields read as "no capture"), never fall back to all-defaults.
        var store = NewStore();
        await File.WriteAllTextAsync(
            store.FilePath,
            "{\"Version\":1,\"OutputDeviceId\":\"7\",\"BufferMilliseconds\":50,"
            + "\"MidiControllerInputName\":\"Push\",\"MidiFeedbackOutputName\":null}");

        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("7", loaded.Audio.OutputDeviceId);
        Assert.Equal(50, loaded.Audio.BufferMilliseconds);
        Assert.Equal("Push", loaded.Midi.ControllerInputName);
        Assert.Null(loaded.Audio.CaptureDeviceId);
        Assert.Null(loaded.Audio.CaptureSource);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsWaveformZoom()
    {
        var store = NewStore();
        var settings = AppSettings.Default with { Visuals = new VisualsSettings(WaveformZoomSeconds: 12.0) };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(12.0, loaded.Visuals.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public async Task Save_ClampsWaveformZoomOutOfRange()
    {
        var store = NewStore();
        var settings = AppSettings.Default with { Visuals = new VisualsSettings(WaveformZoomSeconds: 999.0) };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(VisualsSettings.MaxZoomSeconds, loaded.Visuals.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsNudgeSeconds()
    {
        var store = NewStore();
        var settings = AppSettings.Default with
        {
            Visuals = new VisualsSettings(WaveformZoomSeconds: 7.0, NudgeSeconds: 0.25),
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(0.25, loaded.Visuals.NudgeSeconds, precision: 6);
    }

    [Fact]
    public async Task Load_OlderFileWithoutWaveformZoom_UsesDefault()
    {
        // Back-compat: a file written before the waveform-zoom field existed must read the default zoom,
        // not a zero/unusable value (global #20/#22).
        var store = NewStore();
        await File.WriteAllTextAsync(
            store.FilePath,
            "{\"Version\":2,\"OutputDeviceId\":\"7\",\"BufferMilliseconds\":50,"
            + "\"MidiControllerInputName\":null,\"MidiFeedbackOutputName\":null}");

        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(VisualsSettings.DefaultZoomSeconds, loaded.Visuals.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsLogLevel()
    {
        var store = NewStore();
        var settings = AppSettings.Default with { Diagnostics = new DiagnosticsSettings("Error") };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("Error", loaded.Diagnostics.MinimumLevel);
    }

    [Fact]
    public async Task Load_OlderFileWithoutLogLevel_UsesWarningDefault()
    {
        // Back-compat: a file written before the diagnostics field existed must read the Warning default.
        var store = NewStore();
        await File.WriteAllTextAsync(
            store.FilePath,
            "{\"Version\":2,\"OutputDeviceId\":null,\"BufferMilliseconds\":40,"
            + "\"MidiControllerInputName\":null,\"MidiFeedbackOutputName\":null}");

        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(DiagnosticsSettings.DefaultMinimumLevel, loaded.Diagnostics.MinimumLevel);
        Assert.Equal("Warning", loaded.Diagnostics.MinimumLevel);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsVuMeterBackgroundImagePath()
    {
        var store = NewStore();
        var settings = AppSettings.Default with
        {
            Addons = new AddonSettings(@"C:\faces\brass-dial.png"),
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(@"C:\faces\brass-dial.png", loaded.Addons.VuMeterBackgroundImagePath);
    }

    [Fact]
    public async Task Load_OlderFileWithoutAddons_UsesDefault()
    {
        // Back-compat: a file written before the add-on settings existed must read the built-in face
        // (null custom path), not break the load (global #20/#22).
        var store = NewStore();
        await File.WriteAllTextAsync(
            store.FilePath,
            "{\"Version\":2,\"OutputDeviceId\":null,\"BufferMilliseconds\":40,"
            + "\"MidiControllerInputName\":null,\"MidiFeedbackOutputName\":null}");

        AppSettings loaded = await store.LoadAsync();

        Assert.Null(loaded.Addons.VuMeterBackgroundImagePath);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsWindowLayout()
    {
        var store = NewStore();
        var settings = AppSettings.Default with
        {
            WindowLayout = new WindowLayoutSettings(
                ActiveTabId: "DJ", Width: 1600, Height: 900, X: 120, Y: 80, IsFullScreen: false),
        };

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("DJ", loaded.WindowLayout.ActiveTabId);
        Assert.Equal(1600, loaded.WindowLayout.Width);
        Assert.Equal(900, loaded.WindowLayout.Height);
        Assert.Equal(120, loaded.WindowLayout.X);
        Assert.Equal(80, loaded.WindowLayout.Y);
        Assert.False(loaded.WindowLayout.IsFullScreen);
    }

    [Fact]
    public async Task Load_OlderFileWithoutWindowLayout_OpensFullScreenOnFirstTab()
    {
        // Back-compat: a file written before the window-layout fields existed must read the launch
        // default — full-screen, first tab, default size — not break the load (global #20/#22).
        var store = NewStore();
        await File.WriteAllTextAsync(
            store.FilePath,
            "{\"Version\":2,\"OutputDeviceId\":null,\"BufferMilliseconds\":40,"
            + "\"MidiControllerInputName\":null,\"MidiFeedbackOutputName\":null}");

        AppSettings loaded = await store.LoadAsync();

        Assert.Null(loaded.WindowLayout.ActiveTabId);
        Assert.True(loaded.WindowLayout.IsFullScreen);
        Assert.Equal(WindowLayoutSettings.DefaultWidth, loaded.WindowLayout.Width);
        Assert.Equal(WindowLayoutSettings.DefaultHeight, loaded.WindowLayout.Height);
    }

    [Fact]
    public async Task Load_IncompatibleVersion_ReturnsDefaultsWithWarning()
    {
        string warning = string.Empty;
        var store = new JsonSettingsStore(_root, onWarning: w => warning = w);
        await File.WriteAllTextAsync(
            store.FilePath, "{\"Version\":999,\"OutputDeviceId\":\"x\",\"BufferMilliseconds\":40}");

        AppSettings settings = await store.LoadAsync();

        Assert.Equal(AppSettings.Default, settings);
        Assert.Contains("version", warning);
    }
}
