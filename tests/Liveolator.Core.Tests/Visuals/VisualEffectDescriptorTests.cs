using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class VisualEffectDescriptorTests
{
    // Mirrors the options ExtensionContentLoader uses to read visual-effects.json.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Role_DefaultsToEffect_WhenAbsentFromJson()
    {
        const string json = """
            {
              "EffectId": "com.example.fx/echo",
              "Version": "1.0.0",
              "PackageId": "com.example.fx",
              "ShaderPath": "shaders/echo.frag",
              "Parameters": [ { "Id": "feedback", "Uniform": "uFeedback", "Min": 0, "Max": 1, "Default": 0.5 } ]
            }
            """;

        VisualEffectDescriptor? descriptor = JsonSerializer.Deserialize<VisualEffectDescriptor>(json, JsonOptions);

        Assert.NotNull(descriptor);
        Assert.Equal(VisualEffectRole.Effect, descriptor!.Role);
        Assert.Equal("uFeedback", descriptor.Parameters[0].Uniform);
    }

    [Fact]
    public void Role_Generator_RoundTripsThroughJson()
    {
        var original = new VisualEffectDescriptor(
            "com.example.meters/vu",
            "1.0.0",
            "com.example.meters",
            "shaders/vu-meter.frag",
            new[] { new VisualEffectParameter("redline", "uRedline", 0, 1, 0.8) },
            Role: VisualEffectRole.Generator);

        string json = JsonSerializer.Serialize(original, JsonOptions);
        VisualEffectDescriptor? round = JsonSerializer.Deserialize<VisualEffectDescriptor>(json, JsonOptions);

        Assert.NotNull(round);
        Assert.Equal(VisualEffectRole.Generator, round!.Role);
        Assert.Contains("Generator", json);
        Assert.Equal(original.EffectId, round.EffectId);
        Assert.Equal(original.ShaderPath, round.ShaderPath);
        Assert.Equal("uRedline", round.Parameters[0].Uniform);
    }
}
