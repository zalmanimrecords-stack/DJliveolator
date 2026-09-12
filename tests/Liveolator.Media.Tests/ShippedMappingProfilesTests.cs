using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Media;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The shipped controller-mapping folder (doc 05): every readable profile in it loads, the format is
/// the same one <see cref="MappingProfilePortability"/> exports, and a corrupt, wrong-version or
/// empty file is skipped with a warning instead of stopping the app from starting.
/// </summary>
public sealed class ShippedMappingProfilesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "liveolator-shipped-maps-tests", Guid.NewGuid().ToString("N"));

    private readonly List<string> _warnings = new();

    public ShippedMappingProfilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static ControllerMappingProfile Sample(string name, string hint) => new(
        name, hint,
        new[]
        {
            new ControllerBinding(
                MidiMessageType.NoteOn, 0, 11, PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary),
        });

    private IReadOnlyList<ControllerMappingProfile> Load() =>
        ShippedMappingProfiles.LoadFrom(_dir, _warnings.Add);

    private void WriteSnapshot(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_dir, fileName), json);

    [Fact]
    public async Task Loads_a_profile_exported_by_the_portability_seam()
    {
        // The contract worth pinning: what a performer exports is byte-for-byte what we can ship.
        ControllerMappingProfile exported = Sample("DDJ-400 (community)", "DDJ-400");
        await new MappingProfilePortability().ExportAsync(exported, Path.Combine(_dir, "ddj-400.json"));

        ControllerMappingProfile loaded = Assert.Single(Load());

        Assert.Equal(exported.Name, loaded.Name);
        Assert.Equal(exported.DeviceHint, loaded.DeviceHint);
        Assert.Equal(exported.Bindings.Single().Action, loaded.Bindings.Single().Action);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task Skips_a_corrupt_file_and_keeps_the_readable_ones()
    {
        await new MappingProfilePortability().ExportAsync(Sample("Good", "Good"), Path.Combine(_dir, "b-good.json"));
        WriteSnapshot("a-corrupt.json", "{ this is not json");

        ControllerMappingProfile loaded = Assert.Single(Load());

        Assert.Equal("Good", loaded.Name);
        Assert.Contains(_warnings, w => w.Contains("a-corrupt.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Skips_a_snapshot_written_by_a_future_version()
    {
        WriteSnapshot("future.json",
            "{\"Version\": " + (MappingProfileSnapshot.CurrentVersion + 1)
            + ", \"Profile\": {\"Name\": \"X\", \"DeviceHint\": \"X\", \"Bindings\": []}}");

        Assert.Empty(Load());
        Assert.Contains(_warnings, w => w.Contains("future.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Skips_a_snapshot_with_no_profile_in_it()
    {
        WriteSnapshot("empty.json", "{\"Version\": " + MappingProfileSnapshot.CurrentVersion + "}");

        Assert.Empty(Load());
        Assert.Contains(_warnings, w => w.Contains("empty.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Orders_profiles_by_file_name_so_the_catalog_is_deterministic()
    {
        var portability = new MappingProfilePortability();
        await portability.ExportAsync(Sample("Second", "Second"), Path.Combine(_dir, "b.json"));
        await portability.ExportAsync(Sample("First", "First"), Path.Combine(_dir, "a.json"));

        Assert.Equal(new[] { "First", "Second" }, Load().Select(p => p.Name));
    }

    [Fact]
    public void Ignores_non_json_files()
    {
        File.WriteAllText(Path.Combine(_dir, "README.md"), "# drop your .json profiles here");

        Assert.Empty(Load());
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_missing_folder_is_not_an_error()
    {
        Assert.Empty(ShippedMappingProfiles.LoadFrom(Path.Combine(_dir, "absent"), _warnings.Add));
        Assert.Empty(_warnings);
    }
}
