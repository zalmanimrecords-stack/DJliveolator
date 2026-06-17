using Liveolator.Core.Beat;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Resolves the strobe on/off gate from the shared <see cref="BeatClockState"/> (doc 08 — the
/// VJ strobe is beat-locked, not a free-running timer). Pure math with no GL, so the on/off cycle
/// is unit-tested off the GPU exactly like <see cref="FrameUniforms"/>'s beat flash. The gate is a
/// multiplier the compositor applies to the final brightness: 1.0 passes the frame through, 0.0
/// blacks it out.
///
/// The cycle runs once per beat off <see cref="BeatClockState.BeatPhase"/>: the output is bright for
/// the first <c>onFraction</c> of each beat and black for the remainder, so it pulses on the grid the
/// audio drives. It is confidence-gated the same way the beat flash is — an untrustworthy clock leaves
/// the gate fully open rather than flickering the whole show on a shaky grid (doc 08 risks).
/// </summary>
public static class StrobeGate
{
    /// <summary>Default minimum beat confidence required before the strobe is allowed to gate.</summary>
    public const double DefaultMinConfidence = 0.5;

    /// <summary>Default fraction of each beat the strobe is lit (the ON window).</summary>
    public const double DefaultOnFraction = 0.5;

    // The smallest ON window we honor; below this the strobe would be effectively always-off, which is
    // indistinguishable from disabled and just wastes the gate. Keeps the window in (0, 1].
    private const double MinOnFraction = 1e-3;

    /// <summary>
    /// The brightness multiplier the strobe contributes this frame.
    /// </summary>
    /// <param name="beat">The latest immutable beat snapshot.</param>
    /// <param name="strobeOn">Whether the strobe latch is engaged.</param>
    /// <param name="onFraction">Fraction of each beat the strobe is lit, in (0, 1].</param>
    /// <param name="minConfidence">Minimum confidence before the strobe is allowed to gate.</param>
    /// <returns>1.0 when the frame passes (strobe off, low confidence, or within the ON window); 0.0 when blacked.</returns>
    public static double Resolve(
        BeatClockState beat,
        bool strobeOn,
        double onFraction = DefaultOnFraction,
        double minConfidence = DefaultMinConfidence)
    {
        ArgumentNullException.ThrowIfNull(beat);

        // A released latch or an untrustworthy clock leaves the output untouched — no strobing.
        if (!strobeOn || beat.Confidence < minConfidence)
            return 1.0;

        double window = Math.Clamp(onFraction, MinOnFraction, 1.0);
        double phase = Math.Clamp(beat.BeatPhase, 0.0, 1.0);

        // ON window is [0, window): bright at the top of each beat, black for the rest.
        return phase < window ? 1.0 : 0.0;
    }
}
