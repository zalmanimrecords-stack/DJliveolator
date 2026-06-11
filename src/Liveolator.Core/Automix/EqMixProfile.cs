namespace Liveolator.Core.Automix;

/// <summary>
/// EQ MIX — the flagship bass-swap blend (doc 11): the incoming deck enters with its low band
/// killed while its tops/mids blend in under the outgoing track; the low band hands over in exactly
/// one beat at the transition midpoint; then the outgoing deck shelves away. Two full bass lines
/// never play together — the cardinal sin this style exists to prevent.
/// </summary>
/// <remarks>
/// The swap is anchored at progress 0.5 and spans one beat (1/<see cref="AutomixTransitionShape.BeatsTotal"/>).
/// Because every duration detent is an even bar count and the transition starts on a downbeat, the
/// midpoint IS a downbeat — the swap is bar-quantized by construction, with no extra state.
/// </remarks>
public sealed class EqMixProfile : IAutomixStyleProfile
{
    private const double Flat = 0.5;   // mixer EQ midpoint = no boost/cut
    private const double Kill = 0.0;   // mixer EQ floor = band killed
    private const double Tucked = 0.35; // tops/mids entry level: present but under the outgoing track

    /// <inheritdoc />
    public AutomixFrame Evaluate(double progress, AutomixTransitionShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        double swapStart = 0.5;
        double swapEnd = shape.BeatsTotal > 0.0
            ? Math.Min(1.0, 0.5 + (1.0 / shape.BeatsTotal))
            : 0.6;

        // Crossfader: blend the incoming tops in to center, hold through the swap, then complete.
        double crossfader = progress < 0.4
            ? AutomixRamp.Linear(progress, 0.0, 0.4, shape.FromSide, 0.5)
            : AutomixRamp.Linear(progress, 0.6, 1.0, 0.5, shape.ToSide);

        return new AutomixFrame(
            Crossfader: crossfader,
            // The one-beat bass hand-over, complementary smoothstep — never two basses at once.
            FromLow: AutomixRamp.Smooth(progress, swapStart, swapEnd, Flat, Kill),
            ToLow: AutomixRamp.Smooth(progress, swapStart, swapEnd, Kill, Flat),
            // Incoming tops/mids ride up to unity before the swap…
            ToMid: AutomixRamp.Linear(progress, 0.0, 0.4, Tucked, Flat),
            ToHigh: AutomixRamp.Linear(progress, 0.0, 0.4, Tucked, Flat),
            // …and the outgoing tops/mids shelve down after it, finishing fully out.
            FromMid: OutgoingShelf(progress),
            FromHigh: OutgoingShelf(progress));
    }

    private static double OutgoingShelf(double progress) => progress < 0.95
        ? AutomixRamp.Linear(progress, 0.6, 0.95, Flat, Tucked)
        : AutomixRamp.Linear(progress, 0.95, 1.0, Tucked, Kill);
}
