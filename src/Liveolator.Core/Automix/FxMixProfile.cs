namespace Liveolator.Core.Automix;

/// <summary>
/// FX MIX — the filter-sweep exit, built only on the existing single-knob per-deck filter (no FX
/// engine needed): the outgoing track high-passes upward and "lifts away" while the incoming deck
/// descends in and takes the floor; the outgoing low band is killed at the midpoint swap so a
/// high-passed bass never fights the incoming one. Echo/reverb tails join when a real FX engine
/// lands (the AudioFx action kinds exist; the host does not yet).
/// </summary>
public sealed class FxMixProfile : IAutomixStyleProfile
{
    private const double FilterOff = 0.5;     // single-knob filter centre = bypass
    private const double FilterLifted = 0.85; // high-pass swept up: thin, airy exit
    private const double FilterEntry = 0.62;  // incoming starts slightly high-passed, "descends in"

    /// <inheritdoc />
    public AutomixFrame Evaluate(double progress, AutomixTransitionShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        double swapStart = 0.5;
        double swapEnd = shape.BeatsTotal > 0.0
            ? Math.Min(1.0, 0.5 + (1.0 / shape.BeatsTotal))
            : 0.6;

        return new AutomixFrame(
            // Equal-power travel compressed into the middle of the transition.
            Crossfader: AutomixRamp.Linear(progress, 0.1, 0.8, shape.FromSide, shape.ToSide),
            // The outgoing track thins out and lifts away…
            FromFilter: AutomixRamp.Linear(progress, 0.2, 0.9, FilterOff, FilterLifted),
            // …its bass is killed at the quantized midpoint (a high-passed bass still fights)…
            FromLow: AutomixRamp.Smooth(progress, swapStart, swapEnd, 0.5, 0.0),
            // …while the incoming deck opens from a slight high-pass to full.
            ToFilter: AutomixRamp.Linear(progress, 0.0, 0.5, FilterEntry, FilterOff));
    }
}
