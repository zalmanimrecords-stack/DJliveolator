using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class FrameUniformsTests
{
    private static VisualMacro Brightness(double min = 0, double max = 2, double @default = 1)
        => new("brightness", min, max, @default, new MacroTarget(0, "brightness"));

    private static BeatClockState Beat(
        double confidence = 1.0, double beatPhase = 0.0, bool isBeat = false)
        => BeatClockState.Idle with
        {
            Bpm = 120,
            Confidence = confidence,
            BeatPhase = beatPhase,
            IsBeat = isBeat,
        };

    [Fact]
    public void Resolve_passes_audio_level_through_to_uniforms()
    {
        var level = new VisualAudioLevel(Rms: 0.4, Peak: 0.9, Vu: 0.55);

        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 0.5, Beat(), flashStrength: 0, blackout: false, level: level);

        Assert.Equal(0.4f, u.Rms, 5);
        Assert.Equal(0.9f, u.Peak, 5);
        Assert.Equal(0.55f, u.Level, 5);
    }

    [Fact]
    public void Resolve_defaults_audio_level_to_silent_when_omitted()
    {
        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 0.5, Beat(), flashStrength: 0, blackout: false);

        Assert.Equal(0f, u.Rms);
        Assert.Equal(0f, u.Peak);
        Assert.Equal(0f, u.Level);
    }

    [Fact]
    public void Resolve_maps_macro_through_its_range()
    {
        // 0.75 of [0,2] -> 1.5
        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 0.75, Beat(), flashStrength: 0, blackout: false);

        Assert.Equal(1.5f, u.Brightness, 5);
        Assert.Equal(0f, u.BeatFlash);
        Assert.False(u.Blackout);
    }

    [Fact]
    public void Resolve_clamps_normalized_macro_input()
    {
        FrameUniforms over = FrameUniforms.Resolve(Brightness(), 5.0, Beat(), 0, false);
        FrameUniforms under = FrameUniforms.Resolve(Brightness(), -5.0, Beat(), 0, false);

        Assert.Equal(2f, over.Brightness, 5);   // clamped to Max
        Assert.Equal(0f, under.Brightness, 5);  // clamped to Min
    }

    [Fact]
    public void Resolve_uses_unit_brightness_when_no_macro_bound()
    {
        FrameUniforms u = FrameUniforms.Resolve(brightnessMacro: null, 0.0, Beat(), 0, false);

        Assert.Equal(1f, u.Brightness, 5);
    }

    [Fact]
    public void BeatFlash_peaks_on_the_beat_frame_scaled_by_confidence()
    {
        FrameUniforms full = FrameUniforms.Resolve(Brightness(), 0.5, Beat(confidence: 1.0, isBeat: true), flashStrength: 0.6, blackout: false);
        FrameUniforms half = FrameUniforms.Resolve(Brightness(), 0.5, Beat(confidence: 0.5, isBeat: true), flashStrength: 0.6, blackout: false);

        Assert.Equal(0.6f, full.BeatFlash, 5);
        Assert.Equal(0.3f, half.BeatFlash, 5);
    }

    [Fact]
    public void BeatFlash_decays_across_the_beat_phase()
    {
        FrameUniforms quarter = FrameUniforms.Resolve(Brightness(), 0.5, Beat(beatPhase: 0.25, isBeat: false), 0.8, false);
        FrameUniforms threeQuarter = FrameUniforms.Resolve(Brightness(), 0.5, Beat(beatPhase: 0.75, isBeat: false), 0.8, false);

        // decay = 1 - phase, so 0.25 -> 0.6, 0.75 -> 0.2 (strength 0.8, confidence 1).
        Assert.Equal(0.6f, quarter.BeatFlash, 5);
        Assert.Equal(0.2f, threeQuarter.BeatFlash, 5);
        Assert.True(quarter.BeatFlash > threeQuarter.BeatFlash);
    }

    [Fact]
    public void BeatFlash_is_zero_when_confidence_is_zero()
    {
        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 0.5, Beat(confidence: 0.0, isBeat: true), 0.6, false);

        Assert.Equal(0f, u.BeatFlash);
    }

    [Fact]
    public void Blackout_overrides_effective_brightness()
    {
        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 1.0, Beat(confidence: 1.0, isBeat: true), 0.6, blackout: true);

        Assert.True(u.Blackout);
        Assert.Equal(0f, u.EffectiveBrightness);
    }

    [Fact]
    public void EffectiveBrightness_sums_brightness_and_flash_when_not_blacked_out()
    {
        FrameUniforms u = FrameUniforms.Resolve(Brightness(), 0.5, Beat(confidence: 1.0, isBeat: true), 0.6, false);

        Assert.Equal(1.0f + 0.6f, u.EffectiveBrightness, 5);
    }

    [Fact]
    public void Resolve_rejects_negative_flash_strength()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameUniforms.Resolve(Brightness(), 0.5, Beat(), flashStrength: -0.1, blackout: false));

    [Fact]
    public void Resolve_rejects_null_beat_state()
        => Assert.Throws<ArgumentNullException>(
            () => FrameUniforms.Resolve(Brightness(), 0.5, beat: null!, flashStrength: 0.5, blackout: false));
}
