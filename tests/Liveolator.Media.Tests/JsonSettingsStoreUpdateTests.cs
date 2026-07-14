using System;
using System.IO;
using System.Threading.Tasks;
using Liveolator.Core.Settings;
using Liveolator.Media;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Covers persistence of the startup update-check preferences (<see cref="UpdateSettings"/>) — they must
/// round-trip through the flat <c>SettingsSnapshot</c>, and an older file that predates the fields must
/// still load with the safe defaults (back-compat, global standards #20/#22).
/// </summary>
public sealed class JsonSettingsStoreUpdateTests : IDisposable
{
    private readonly string _root;

    public JsonSettingsStoreUpdateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liveolator-update-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    private JsonSettingsStore NewStore() => new(_root);

    [Fact]
    public async Task UpdateSettings_RoundTrip()
    {
        var store = NewStore();
        AppSettings saved = AppSettings.Default with
        {
            Updates = new UpdateSettings(CheckOnStartup: false, SkippedVersion: "0.1.5"),
        };

        await store.SaveAsync(saved);
        AppSettings loaded = await store.LoadAsync();

        Assert.False(loaded.Updates.CheckOnStartup);
        Assert.Equal("0.1.5", loaded.Updates.SkippedVersion);
    }

    [Fact]
    public async Task OlderFileWithoutUpdateFields_LoadsSafeDefaults()
    {
        // A version-2 file written before the update fields existed (they are simply absent).
        const string legacy = """
        {
          "Version": 2,
          "OutputDeviceId": null,
          "BufferMilliseconds": 40,
          "MidiControllerInputName": null,
          "MidiFeedbackOutputName": null
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), legacy);

        AppSettings loaded = await NewStore().LoadAsync();

        Assert.True(loaded.Updates.CheckOnStartup); // default: check enabled
        Assert.Null(loaded.Updates.SkippedVersion);
    }
}
