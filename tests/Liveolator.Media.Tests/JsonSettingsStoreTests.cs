using System;
using System.IO;
using System.Threading.Tasks;
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
