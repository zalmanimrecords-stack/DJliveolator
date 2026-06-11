namespace Liveolator.Core.Automix;

/// <summary>
/// A pure auto-mix automation profile: maps transition progress 0..1 to the mixer parameters the
/// style wants (doc 11 Auto-Mix). Implementations are stateless — the controller owns time and
/// state; profiles own only the curves, so each is exhaustively unit-testable as a function.
/// </summary>
public interface IAutomixStyleProfile
{
    /// <summary>Evaluate the style at <paramref name="progress"/> (clamped 0..1 by the caller).</summary>
    AutomixFrame Evaluate(double progress, AutomixTransitionShape shape);
}
