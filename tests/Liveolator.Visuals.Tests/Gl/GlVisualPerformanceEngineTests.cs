using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class GlVisualPerformanceEngineTests
{
    private static VisualMacro Brightness()
        => new(GlVisualPerformanceEngine.BrightnessMacro, 0, 2, 1, new MacroTarget(0, "brightness"));

    private static VisualBank BankWithImage(string imagePath = "frame.png")
    {
        var layer = new VisualLayer(
            "base",
            new VisualSourceRef(VisualSourceKind.Image, imagePath),
            Array.Empty<EffectRef>(),
            BlendMode.Normal,
            opacity: 1.0);
        var scene = new VisualScene(
            "scene-1",
            new[] { layer },
            new Dictionary<string, double>(),
            TransitionStyle.Cut,
            BeatBehavior.None);
        return new VisualBank("bank-1", new[] { scene });
    }

    private static GlVisualPerformanceEngine NewEngine(FakeBeatClock clock, double flashStrength = 0.6)
        => new(BankWithImage(), Brightness(), clock, flashStrength);

    [Fact]
    public void CurrentFrame_starts_at_the_macro_default_brightness()
    {
        var engine = NewEngine(new FakeBeatClock());

        // Default 1 within [0,2]; no beat -> no flash.
        Assert.Equal(1f, engine.CurrentFrame().Brightness, 5);
        Assert.Equal(0f, engine.CurrentFrame().BeatFlash);
    }

    [Fact]
    public void SetMacro_drives_the_brightness_uniform()
    {
        var engine = NewEngine(new FakeBeatClock());

        engine.SetMacro(GlVisualPerformanceEngine.BrightnessMacro, 0.25); // 0.25 of [0,2] = 0.5

        Assert.Equal(0.5f, engine.CurrentFrame().Brightness, 5);
    }

    [Fact]
    public void SetMacro_clamps_out_of_range_values()
    {
        var engine = NewEngine(new FakeBeatClock());

        engine.SetMacro(GlVisualPerformanceEngine.BrightnessMacro, 9.0);

        Assert.Equal(2f, engine.CurrentFrame().Brightness, 5);
    }

    [Fact]
    public void SetMacro_rejects_an_empty_name()
    {
        var engine = NewEngine(new FakeBeatClock());

        Assert.Throws<ArgumentException>(() => engine.SetMacro(" ", 0.5));
    }

    [Fact]
    public void CurrentFrame_flashes_on_a_confident_beat()
    {
        var clock = new FakeBeatClock();
        var engine = NewEngine(clock, flashStrength: 0.6);

        clock.Current = BeatClockState.Idle with { Confidence = 1.0, IsBeat = true };

        Assert.Equal(0.6f, engine.CurrentFrame().BeatFlash, 5);
    }

    [Fact]
    public void Blackout_zeros_effective_brightness_and_releases()
    {
        var clock = new FakeBeatClock();
        var engine = NewEngine(clock);

        engine.Blackout(true);
        Assert.True(engine.CurrentFrame().Blackout);
        Assert.Equal(0f, engine.CurrentFrame().EffectiveBrightness);

        engine.Blackout(false);
        Assert.False(engine.CurrentFrame().Blackout);
    }

    [Fact]
    public void ActiveBank_is_the_bank_supplied_at_construction()
    {
        var engine = NewEngine(new FakeBeatClock());

        Assert.Equal("bank-1", engine.ActiveBank.Name);
        Assert.Equal(1, engine.BankCount);
        Assert.Equal(0, engine.ActiveBankIndex);
    }

    [Fact]
    public void SelectBank_switches_the_active_bank_and_its_composition()
    {
        VisualBank warmup = NamedBank("Warmup", "warm.png");
        VisualBank peak = NamedBank("Peak", "peak.png");
        var engine = new GlVisualPerformanceEngine(new[] { warmup, peak }, Brightness(), new FakeBeatClock());

        Assert.Equal("Warmup", engine.ActiveBank.Name);

        engine.SelectBank(1);

        Assert.Equal(1, engine.ActiveBankIndex);
        Assert.Equal("Peak", engine.ActiveBank.Name);
        // The active scene/composition follows the selected bank's first scene.
        Assert.Equal("peak.png", engine.CurrentComposition()[0].Source.Reference);
    }

    [Fact]
    public void SelectBank_ignores_an_out_of_range_index_without_changing_the_active_bank()
    {
        VisualBank warmup = NamedBank("Warmup", "warm.png");
        VisualBank peak = NamedBank("Peak", "peak.png");
        var engine = new GlVisualPerformanceEngine(new[] { warmup, peak }, Brightness(), new FakeBeatClock());

        engine.SelectBank(1);
        engine.SelectBank(9);  // out of range — ignored
        engine.SelectBank(-1); // out of range — ignored

        Assert.Equal(1, engine.ActiveBankIndex);
        Assert.Equal("Peak", engine.ActiveBank.Name);
    }

    [Fact]
    public void Multi_bank_constructor_rejects_an_empty_or_null_bank_list()
    {
        Assert.Throws<ArgumentException>(
            () => new GlVisualPerformanceEngine(Array.Empty<VisualBank>(), Brightness(), new FakeBeatClock()));
        Assert.Throws<ArgumentNullException>(
            () => new GlVisualPerformanceEngine((IReadOnlyList<VisualBank>)null!, Brightness(), new FakeBeatClock()));
    }

    private static VisualBank NamedBank(string name, string imagePath)
    {
        var layer = new VisualLayer(
            "base", new VisualSourceRef(VisualSourceKind.Image, imagePath),
            Array.Empty<EffectRef>(), BlendMode.Normal, opacity: 1.0);
        var scene = new VisualScene(
            name + "-scene", new[] { layer }, new Dictionary<string, double>(),
            TransitionStyle.Cut, BeatBehavior.None);
        return new VisualBank(name, new[] { scene });
    }

    [Fact]
    public void Deferred_operations_do_not_throw()
    {
        var engine = NewEngine(new FakeBeatClock());
        var scene = engine.ActiveBank.Scenes[0];

        // These are logged no-ops in the slice; they must not break callers.
        engine.LoadScene(scene, Quantize.NextBar);
        engine.SetLayerSource(0, new VisualSourceRef(VisualSourceKind.Image, "x.png"), Quantize.Immediate);
        engine.ToggleLayer(0);
        engine.SetLayerOpacity(0, 0.5);
        engine.LaunchClip(0, "clip", Quantize.NextBeat);
        engine.Strobe(true);
        engine.Transition(TransitionStyle.Cut, Quantize.Immediate);
    }

    [Fact]
    public void LoadScene_replaces_the_live_composition_and_marks_it_dirty()
    {
        var engine = NewEngine(new FakeBeatClock());
        long before = engine.CompositionVersion;
        VisualScene replacement = NamedBank("Replacement", "replacement.png").Scenes[0];

        engine.LoadScene(replacement, Quantize.Immediate);

        Assert.Equal("replacement.png", engine.CurrentComposition()[0].Source.Reference);
        Assert.True(engine.CompositionVersion > before);
    }

    [Fact]
    public void Layer_mutations_update_the_live_composition()
    {
        var engine = NewEngine(new FakeBeatClock());

        engine.SetLayerOpacity(0, 0.35);
        Assert.Equal(0.35, engine.CurrentComposition()[0].Opacity, precision: 6);

        engine.ToggleLayer(0);
        Assert.Equal(0.0, engine.CurrentComposition()[0].Opacity, precision: 6);

        engine.SetLayerSource(
            0, new VisualSourceRef(VisualSourceKind.Image, "replacement.png"), Quantize.Immediate);
        Assert.Equal("replacement.png", engine.CurrentComposition()[0].Source.Reference);
    }

    [Fact]
    public void Constructor_rejects_negative_flash_strength()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new GlVisualPerformanceEngine(BankWithImage(), Brightness(), new FakeBeatClock(), flashStrength: -1));

    [Fact]
    public void CurrentComposition_resolves_the_single_image_layer_as_renderable()
    {
        var engine = NewEngine(new FakeBeatClock());

        IReadOnlyList<ResolvedLayer> layers = engine.CurrentComposition();

        Assert.Single(layers);
        Assert.Equal("base", layers[0].Name);
        Assert.True(layers[0].Renderable);
        Assert.Equal(BlendMode.Normal, layers[0].Blend);
    }

    [Fact]
    public void CurrentComposition_resolves_a_multi_layer_scene_in_order_with_blend_and_opacity()
    {
        VisualBank bank = BankWithLayers(
            ("base", VisualSourceKind.Image, BlendMode.Normal, 1.0),
            ("glow", VisualSourceKind.Image, BlendMode.Add, 0.4),
            ("clip", VisualSourceKind.VideoClip, BlendMode.Screen, 1.0));
        var engine = new GlVisualPerformanceEngine(bank, Brightness(), new FakeBeatClock());

        IReadOnlyList<ResolvedLayer> layers = engine.CurrentComposition();

        Assert.Equal(3, layers.Count);
        Assert.Equal(BlendMode.Add, layers[1].Blend);
        Assert.Equal(0.4, layers[1].Opacity);
        // The video layer is carried in the composition but flagged non-renderable (deferred source).
        Assert.False(layers[2].Renderable);
        Assert.True(layers[0].Renderable);
    }

    private static VisualBank BankWithLayers(
        params (string Name, VisualSourceKind Kind, BlendMode Blend, double Opacity)[] specs)
    {
        var layers = specs
            .Select(s => new VisualLayer(
                s.Name,
                new VisualSourceRef(s.Kind, s.Name + ".src"),
                Array.Empty<EffectRef>(),
                s.Blend,
                s.Opacity))
            .ToArray();
        var scene = new VisualScene(
            "scene-multi",
            layers,
            new Dictionary<string, double>(),
            TransitionStyle.Cut,
            BeatBehavior.None);
        return new VisualBank("bank-multi", new[] { scene });
    }
}
