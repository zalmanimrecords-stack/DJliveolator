namespace Liveolator.Core.Automix;

/// <summary>
/// The fixed geometry of one transition, captured when it starts: where the crossfader ramp begins
/// (wherever the fader was on engage — no jump), which extreme is 100% the incoming deck, and how
/// many beats the blend spans (length in bars × beats per bar).
/// </summary>
/// <param name="FromSide">Crossfader position the ramp starts from.</param>
/// <param name="ToSide">Crossfader position that is 100% the incoming deck (0 or 1).</param>
/// <param name="BeatsTotal">Total transition length in beats.</param>
public sealed record AutomixTransitionShape(double FromSide, double ToSide, double BeatsTotal);
