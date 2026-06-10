using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

/// <summary>
/// Pins the on-disk contract for a package's <c>presets.json</c> (doc 28). Mirrors the JSON options
/// <c>ExtensionContentLoader</c> uses for <c>visual-effects.json</c> so a hand-authored preset file
/// loads the same way.
/// </summary>
public class GeneratorPresetSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Preset_RoundTripsThroughJson()
    {
        var original = new GeneratorPreset(
            "liveolator.builtin/milkdrop-starter",
            "Milkdrop Starter",
            "liveolator.builtin/milkdrop",
            "1.0.0",
            new[]
            {
                new ControllableParameter("glow", "GLOW"),
                new ControllableParameter("warp", "WARP"),
            });

        string json = JsonSerializer.Serialize(original, JsonOptions);
        GeneratorPreset? round = JsonSerializer.Deserialize<GeneratorPreset>(json, JsonOptions);

        Assert.NotNull(round);
        Assert.Equal(original.PresetId, round!.PresetId);
        Assert.Equal(original.GeneratorEffectId, round.GeneratorEffectId);
        Assert.Equal(2, round.Controllable.Count);
        Assert.Equal("glow", round.Controllable[0].Id);
        Assert.Equal("GLOW", round.Controllable[0].Label);
    }

    [Fact]
    public void Preset_DeserializesFromHandAuthoredJson()
    {
        const string json = """
            {
              "PresetId": "com.example.vis/aurora",
              "Name": "Aurora",
              "GeneratorEffectId": "com.example.vis/aurora-gen",
              "GeneratorVersion": "1.0.0",
              "Controllable": [
                { "Id": "glow", "Label": "GLOW" },
                { "Id": "speed", "Label": "SPEED" }
              ]
            }
            """;

        GeneratorPreset? preset = JsonSerializer.Deserialize<GeneratorPreset>(json, JsonOptions);

        Assert.NotNull(preset);
        Assert.Equal("Aurora", preset!.Name);
        Assert.Equal(2, preset.Controllable.Count);
    }

    [Fact]
    public void Deserializing_PresetWithMoreThanFiveControllable_Throws()
    {
        const string json = """
            {
              "PresetId": "p",
              "Name": "P",
              "GeneratorEffectId": "g",
              "GeneratorVersion": "1.0.0",
              "Controllable": [
                { "Id": "a", "Label": "A" },
                { "Id": "b", "Label": "B" },
                { "Id": "c", "Label": "C" },
                { "Id": "d", "Label": "D" },
                { "Id": "e", "Label": "E" },
                { "Id": "f", "Label": "F" }
              ]
            }
            """;

        // The constructor enforces the ceiling, so deserialization surfaces it (wrapped by STJ).
        Assert.ThrowsAny<System.Exception>(() => JsonSerializer.Deserialize<GeneratorPreset>(json, JsonOptions));
    }
}
