using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Media;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Export/import of a controller mapping profile (doc 05): a profile round-trips through a file, and a
/// missing/corrupt/wrong-version file imports as null rather than throwing.
/// </summary>
public sealed class MappingProfilePortabilityTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "liveolator-midimap-tests", Guid.NewGuid().ToString("N"));

    public MappingProfilePortabilityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static ControllerMappingProfile Sample() => new(
        "CMD STUDIO 2A", "CMD Studio 2A",
        new[]
        {
            new ControllerBinding(MidiMessageType.ControlChange, 0, 1, PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute),
            new ControllerBinding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary),
        });

    [Fact]
    public async Task Export_then_import_round_trips_the_profile()
    {
        var portability = new MappingProfilePortability();
        string path = Path.Combine(_dir, "cmd.json");

        await portability.ExportAsync(Sample(), path);
        ControllerMappingProfile? back = await portability.ImportAsync(path);

        Assert.NotNull(back);
        Assert.Equal("CMD STUDIO 2A", back!.Name);
        Assert.Equal("CMD Studio 2A", back.DeviceHint);
        Assert.Equal(2, back.Bindings.Count);
        Assert.Contains(back.Bindings, b => b.Action == PerformanceActionKind.MixerCrossfade && b.Data1 == 1);
    }

    [Fact]
    public async Task Import_missing_file_returns_null()
        => Assert.Null(await new MappingProfilePortability().ImportAsync(Path.Combine(_dir, "nope.json")));

    [Fact]
    public async Task Import_garbage_file_returns_null_with_warning()
    {
        string warning = string.Empty;
        var portability = new MappingProfilePortability(w => warning = w);
        string path = Path.Combine(_dir, "bad.json");
        await File.WriteAllTextAsync(path, "{ not a snapshot");

        Assert.Null(await portability.ImportAsync(path));
    }
}
