using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// The per-frame inputs the fragment shader needs, resolved from the current macro values and the
/// shared <see cref="BeatClockState"/> (doc 08 — "per-frame reactive parameters"). This is pure
/// math with no GL, so the macro→uniform mapping and the beat-flash response are unit-testable off
/// the GPU; <see cref="LayeredQuadRenderer"/> just pushes these values into the program each frame.
/// </summary>
/// <param name="Brightness">Base brightness multiplier the "brightness" macro drives (>= 0).</param>
/// <param name="BeatFlash">
/// Extra brightness added on beats, decaying across the beat. 0 when not beat-reactive or between
/// beats; peaks on <see cref="BeatClockState.IsBeat"/> scaled by detection confidence.
/// </param>
/// <param name="Blackout">When true the shader outputs black regardless of the other values.</param>
/// <param name="Rms">Live master RMS level, 0..1 (doc 26 — the <c>uRms</c> shader uniform).</param>
/// <param name="Peak">Live master peak level, 0..1 (the <c>uPeak</c> shader uniform).</param>
/// <param name="Level">VU-ballistics level, 0..1 (the <c>uLevel</c> shader uniform — a meter's needle).</param>
public readonly record struct FrameUniforms(
    float Brightness,
    float BeatFlash,
    bool Blackout,
    float BeatPhase = 0,
    float BarPhase = 0,
    float Confidence = 0,
    float Rms = 0,
    float Peak = 0,
    float Level = 0)
{
    /// <summary>The neutral pass-through frame: full brightness, no flash, not blacked out.</summary>
    public static FrameUniforms Neutral { get; } = new(Brightness: 1f, BeatFlash: 0f, Blackout: false);

    /// <summary>The effective output multiplier the shader applies to the sampled texture.</summary>
    public float EffectiveBrightness => Blackout ? 0f : Brightness + BeatFlash;

    /// <summary>
    /// Resolves the frame uniforms from the brightness macro and the beat clock. The macro supplies
    /// a real value via <see cref="VisualMacro.Resolve"/>; the flash is a phase-decaying pulse so the
    /// image punches on each beat and eases back — the strength scaled by beat
    /// <see cref="BeatClockState.Confidence"/> so an unstable clock does not strobe (doc 08 risks).
    /// </summary>
    /// <param name="brightnessMacro">The macro bound to the brightness uniform (may be null → 1.0).</param>
    /// <param name="normalizedBrightness">The macro's current normalized 0..1 control value.</param>
    /// <param name="beat">The latest immutable beat snapshot.</param>
    /// <param name="flashStrength">Peak extra brightness on a fully-confident beat (>= 0).</param>
    /// <param name="blackout">Whether blackout is engaged.</param>
    /// <param name="level">
    /// The live audio level the meter/reactive shaders read (doc 26). Null → <see cref="VisualAudioLevel.Silent"/>,
    /// so headless rendering still resolves a frame with the meter at its floor.
    /// </param>
    public static FrameUniforms Resolve(
        VisualMacro? brightnessMacro,
        double normalizedBrightness,
        BeatClockState beat,
        double flashStrength,
        bool blackout,
        VisualAudioLevel? level = null)
    {
        ArgumentNullException.ThrowIfNull(beat);
        if (flashStrength < 0 || double.IsNaN(flashStrength))
            throw new ArgumentOutOfRangeException(nameof(flashStrength), flashStrength, "Flash strength must be >= 0.");

        double brightness = brightnessMacro?.Resolve(normalizedBrightness) ?? 1.0;
        if (brightness < 0)
            brightness = 0;

        double flash = ResolveFlash(beat, flashStrength);
        VisualAudioLevel audio = level ?? VisualAudioLevel.Silent;

        return new FrameUniforms(
            (float)brightness,
            (float)flash,
            blackout,
            (float)beat.BeatPhase,
            (float)beat.BarPhase,
            (float)beat.Confidence,
            (float)audio.Rms,
            (float)audio.Peak,
            (float)audio.Vu);
    }

    // The flash peaks on the beat frame and decays linearly across the beat via BeatPhase, gated by
    // confidence so a low-confidence clock contributes little — the same guard the audio Quantize uses.
    private static double ResolveFlash(BeatClockState beat, double flashStrength)
    {
        if (flashStrength == 0 || beat.Confidence <= 0)
            return 0;

        double phase = Math.Clamp(beat.BeatPhase, 0.0, 1.0);
        double decay = beat.IsBeat ? 1.0 : Math.Max(0.0, 1.0 - phase);
        return flashStrength * decay * Math.Clamp(beat.Confidence, 0.0, 1.0);
    }
}
