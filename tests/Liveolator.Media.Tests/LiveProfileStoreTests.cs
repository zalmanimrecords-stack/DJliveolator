using System.Text.Json;
using Liveolator.Core.Actions;
using Liveolator.Core.Autopilot;
using Liveolator.Core.Mapping;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Round-trip, version-tolerance, and malformed-file handling for each of the three persisted
/// Live-Mode families (mapping profiles, visual banks/macros, autopilot rule-sets) — doc 13.
/// </summary>
public class LiveProfileStoreTests
{
    // ---- ControllerMappingProfile (doc 05) --------------------------------------------------

    [Fact]
    public async Task MappingProfile_RoundTrips()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        var profile = new ControllerMappingProfile(
            "Push v1",
            "Ableton Push",
            new[]
            {
                new ControllerBinding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualLoadScene,
                    ActionInputMode.Momentary, Slot: 3, Argument: "intro"),
                new ControllerBinding(MidiMessageType.ControlChange, 1, 71, PerformanceActionKind.VisualSetMacro,
                    ActionInputMode.Absolute, Argument: "intensity", Curve: ValueCurve.Logarithmic),
            });

        await store.SaveMappingProfileAsync(profile);
        ControllerMappingProfile? loaded = await store.LoadMappingProfileAsync(profile.Name);

        // Compare members structurally: record equality over IReadOnlyList is reference-based,
        // so a faithful round-trip still produces a distinct (but element-equal) collection.
        Assert.NotNull(loaded);
        Assert.Equal(profile.Name, loaded.Name);
        Assert.Equal(profile.DeviceHint, loaded.DeviceHint);
        Assert.Equal(profile.Bindings, loaded.Bindings);
    }

    [Fact]
    public async Task MappingProfile_OlderSchemaVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        string path = store.MappingProfilePath("legacy");
        await WriteJsonAsync(path, "{\"Version\":0,\"Profile\":{\"Name\":\"legacy\",\"DeviceHint\":\"x\",\"Bindings\":[]}}");

        ControllerMappingProfile? loaded = await store.LoadMappingProfileAsync("legacy");

        Assert.Null(loaded);
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task MappingProfile_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.MappingProfilePath("broken"), "{ not valid json");

        ControllerMappingProfile? loaded = await store.LoadMappingProfileAsync("broken");

        Assert.Null(loaded);
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task MappingProfile_Missing_ReturnsNull_NoWarning()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);

        Assert.Null(await store.LoadMappingProfileAsync("never-saved"));
        Assert.Null(warning); // a missing file is normal, not a fault
    }

    // ---- VisualBank + VisualScenes (doc 08) -------------------------------------------------

    [Fact]
    public async Task VisualBank_RoundTrips()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        var bank = SampleBank();

        await store.SaveVisualBankAsync(bank);
        VisualBank? loaded = await store.LoadVisualBankAsync(bank.Name);

        // Deeply nested (layers, effect-param dictionaries): assert structural equality by
        // re-serializing both, since record equality over collections is reference-based.
        Assert.NotNull(loaded);
        Assert.Equal(Canonical(bank), Canonical(loaded));
    }

    [Fact]
    public async Task VisualBank_OlderSchemaVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.VisualBankPath("old"),
            "{\"Version\":0,\"Bank\":{\"Name\":\"old\",\"Scenes\":[]}}");

        Assert.Null(await store.LoadVisualBankAsync("old"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task VisualBank_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.VisualBankPath("broken"), "}{");

        Assert.Null(await store.LoadVisualBankAsync("broken"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task ListVisualBankNames_WhenNoneSaved_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);

        Assert.Empty(await store.ListVisualBankNamesAsync());
    }

    [Fact]
    public async Task ListVisualBankNames_ReturnsEverySavedBank_OrderedCaseInsensitively()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);

        await store.SaveVisualBankAsync(new VisualBank("Peak", Array.Empty<VisualScene>()));
        await store.SaveVisualBankAsync(new VisualBank("Live", Array.Empty<VisualScene>()));
        await store.SaveVisualBankAsync(new VisualBank("breaks", Array.Empty<VisualScene>()));

        IReadOnlyList<string> names = await store.ListVisualBankNamesAsync();

        Assert.Equal(new[] { "breaks", "Live", "Peak" }, names);
    }

    [Fact]
    public async Task ListVisualBankNames_RoundTripsWithLoad()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        await store.SaveVisualBankAsync(SampleBank()); // named "set-a"

        IReadOnlyList<string> names = await store.ListVisualBankNamesAsync();

        string only = Assert.Single(names);
        Assert.Equal("set-a", only);
        Assert.NotNull(await store.LoadVisualBankAsync(only));
    }

    // ---- VisualMacro definitions (doc 08) ---------------------------------------------------

    [Fact]
    public async Task VisualMacros_RoundTrip()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        var macros = new[]
        {
            new VisualMacro("intensity", 0, 1, 0.5, new MacroTarget(0, "opacity")),
            new VisualMacro("echo", 0, 2, 0.0, new MacroTarget(1, "echo.feedback")),
        };

        await store.SaveVisualMacrosAsync(macros);
        IReadOnlyList<VisualMacro> loaded = await store.LoadVisualMacrosAsync();

        Assert.Equal(macros, loaded);
    }

    [Fact]
    public async Task VisualMacros_ConcurrentSaves_NeverThrow_AndLeaveAReadableFile()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        var macros = new[] { new VisualMacro("intensity", 0, 1, 0.5, new MacroTarget(0, "opacity")) };

        // Fire many overlapping saves to the SAME path. The save gate + unique temp file must serialize
        // them so none throw on a shared temp and the live file is never left corrupt (doc 27 medium fix).
        var saves = new System.Collections.Generic.List<Task>();
        for (int i = 0; i < 16; i++)
            saves.Add(store.SaveVisualMacrosAsync(macros));
        await Task.WhenAll(saves);

        IReadOnlyList<VisualMacro> loaded = await store.LoadVisualMacrosAsync();
        Assert.Equal(macros, loaded); // one intact write survived — not a torn/partial file
    }

    [Fact]
    public async Task VisualMacros_OlderSchemaVersion_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.VisualMacrosPath, "{\"Version\":0,\"Macros\":[]}");

        Assert.Empty(await store.LoadVisualMacrosAsync());
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task VisualMacros_CorruptFile_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.VisualMacrosPath, "not json at all");

        Assert.Empty(await store.LoadVisualMacrosAsync());
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task VisualMacros_Missing_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);

        Assert.Empty(await store.LoadVisualMacrosAsync());
    }

    // ---- AutopilotRuleSet (doc 10) ----------------------------------------------------------

    [Fact]
    public async Task AutopilotRuleSet_RoundTrips()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);
        var ruleSet = SampleRuleSet();

        await store.SaveAutopilotRuleSetAsync(ruleSet);
        AutopilotRuleSet? loaded = await store.LoadAutopilotRuleSetAsync(ruleSet.Name);

        // Rules + scene-pool collections are reference-compared by record equality; assert
        // structural equality by re-serializing both.
        Assert.NotNull(loaded);
        Assert.Equal(Canonical(ruleSet), Canonical(loaded));
    }

    [Fact]
    public async Task AutopilotRuleSet_OlderSchemaVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.AutopilotRuleSetPath("old"),
            "{\"Version\":0,\"RuleSet\":{\"Name\":\"old\",\"Rules\":[],\"ScenePool\":{\"SceneNames\":[],\"CooldownBars\":0}}}");

        Assert.Null(await store.LoadAutopilotRuleSetAsync("old"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task AutopilotRuleSet_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new LiveProfileStore(dir.Path, onWarning: w => warning = w);
        await WriteJsonAsync(store.AutopilotRuleSetPath("broken"), "{{{");

        Assert.Null(await store.LoadAutopilotRuleSetAsync("broken"));
        Assert.NotNull(warning);
    }

    // ---- layout / safety --------------------------------------------------------------------

    [Fact]
    public async Task Save_UsesLiveSubfolders_AndIsAtomic()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);

        await store.SaveMappingProfileAsync(ControllerMappingProfile.Empty("Push v1", "Push"));

        string path = store.MappingProfilePath("Push v1");
        Assert.Contains(Path.Combine("live", "mappings"), path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task ProfileName_WithPathSeparators_CannotEscapeFolder()
    {
        using var dir = new TempDirectory();
        var store = new LiveProfileStore(dir.Path);

        // A hostile name must not write outside live/mappings (global standards #17/#19).
        await store.SaveAutopilotRuleSetAsync(
            SampleRuleSet() with { Name = "../../evil" });

        string sanitized = store.AutopilotRuleSetPath("../../evil");
        Assert.DoesNotContain("..", Path.GetFileName(sanitized));
        Assert.True(File.Exists(sanitized));
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.json")));
    }

    private static VisualBank SampleBank()
    {
        var layer = new VisualLayer(
            "base",
            new VisualSourceRef(VisualSourceKind.VideoClip, "clip-1"),
            new[] { new EffectRef("echo", new Dictionary<string, double> { ["feedback"] = 0.5 }) },
            BlendMode.Add,
            0.8);
        var scene = new VisualScene(
            "intro",
            new[] { layer },
            new Dictionary<string, double> { ["intensity"] = 0.4 },
            TransitionStyle.Crossfade,
            new BeatBehavior(PulseOnBeat: true, PulseOnDownbeat: false, LaunchEveryBars: 4));
        return new VisualBank("set-a", new[] { scene });
    }

    private static AutopilotRuleSet SampleRuleSet()
        => new(
            "house-set",
            new[]
            {
                new AutopilotRule(
                    "drop-scene",
                    new RuleTrigger(TriggerKind.EveryNBars, 8),
                    new RuleCondition(MinEnergy: 0.6),
                    new PerformanceAction(PerformanceActionKind.VisualLoadScene, ActionInputMode.Momentary),
                    new Cooldown(4)),
            },
            new ScenePool(new[] { "intro", "drop" }, CooldownBars: 2),
            Seed: 42,
            OverridePolicy: new AutopilotOverridePolicy(OverrideMode.AutoResume, ResumeAfterBars: 3));

    /// <summary>Serializes a value to JSON for structural comparison of deeply nested records.</summary>
    private static string Canonical<T>(T value) => JsonSerializer.Serialize(value);

    private static async Task WriteJsonAsync(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }
}
