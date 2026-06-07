using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class EffectParameterResolverTests
{
    [Fact]
    public void Resolve_UsesDefaults_ThenAppliesTargetedMacro()
    {
        const string instanceId = "fx-1";
        var effect = new EffectRef(
            "core/hue",
            "1.0.0",
            instanceId,
            new Dictionary<string, double> { ["amount"] = 0.2 });
        var descriptor = new VisualEffectDescriptor(
            "core/hue",
            "1.0.0",
            "core",
            "hue.frag",
            new[] { new VisualEffectParameter("amount", "uAmount", 0, 2, 1) });
        var registry = new VisualEffectRegistry();
        registry.ReplacePackage("core", new[] { descriptor });
        var macro = new VisualMacro(
            "hue",
            0,
            2,
            0,
            new MacroTarget(0, instanceId, "amount"));

        IReadOnlyList<ResolvedEffectParameters> resolved = EffectParameterResolver.Resolve(
            layer: 0,
            new[] { effect },
            registry,
            new[] { macro },
            new Dictionary<string, double> { ["hue"] = 0.75 });

        ResolvedEffectParameters only = Assert.Single(resolved);
        Assert.Equal(1.5f, only.Uniforms["uAmount"]);
    }

    [Fact]
    public void Resolve_MissingDescriptor_SkipsEffect()
    {
        var effect = new EffectRef("missing", new Dictionary<string, double>());

        Assert.Empty(EffectParameterResolver.Resolve(
            0,
            new[] { effect },
            new VisualEffectRegistry(),
            Array.Empty<VisualMacro>(),
            new Dictionary<string, double>()));
    }
}
