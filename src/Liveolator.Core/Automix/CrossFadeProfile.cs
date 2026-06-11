namespace Liveolator.Core.Automix;

/// <summary>
/// CROSS FADE — the forgiving default style and the degraded fallback when a beat grid is missing:
/// the crossfader travels linearly from the outgoing extreme to the incoming extreme and nothing
/// else moves. Constant perceived power comes from the mixer's existing equal-power
/// <c>CrossfaderCurve.Smooth</c>, not from this profile (one curve definition, doc 11).
/// </summary>
public sealed class CrossFadeProfile : IAutomixStyleProfile
{
    /// <inheritdoc />
    public AutomixFrame Evaluate(double progress, AutomixTransitionShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return new AutomixFrame(
            Crossfader: AutomixRamp.Linear(progress, 0.0, 1.0, shape.FromSide, shape.ToSide));
    }
}
