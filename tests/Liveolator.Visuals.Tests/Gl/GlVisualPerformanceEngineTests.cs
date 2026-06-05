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
        engine.SelectBank(3);
    }

    [Fact]
    public void Constructor_rejects_negative_flash_strength()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new GlVisualPerformanceEngine(BankWithImage(), Brightness(), new FakeBeatClock(), flashStrength: -1));
}
