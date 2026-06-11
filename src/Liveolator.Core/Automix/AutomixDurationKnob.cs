namespace Liveolator.Core.Automix;

/// <summary>
/// Maps the AUTOMIX time knob (normalized 0..1) to a musical bar-count detent. The transition length
/// is in BARS, not seconds — the engine rides the shared beat clock, so a length chosen in bars ends
/// on a phrase boundary at any tempo (doc 11). Detents are all even bar counts on purpose: the styles
/// quantize their bass-swap to the transition midpoint, and an even bar count puts that midpoint on a
/// downbeat by construction.
/// </summary>
public static class AutomixDurationKnob
{
    /// <summary>The selectable transition lengths, in bars.</summary>
    public static readonly IReadOnlyList<int> DetentBars = new[] { 2, 4, 8, 16, 32, 64 };

    /// <summary>The default transition length (bars) — a comfortable 16-bar phrase blend.</summary>
    public const int DefaultBars = 16;

    /// <summary>Resolve a 0..1 knob position to the nearest bar detent.</summary>
    public static int BarsFor(double knobPosition)
    {
        double clamped = Math.Clamp(knobPosition, 0.0, 1.0);
        int index = (int)Math.Round(clamped * (DetentBars.Count - 1));
        return DetentBars[index];
    }

    /// <summary>The 0..1 knob position representing a bar detent (nearest detent when between).</summary>
    public static double KnobFor(int bars)
    {
        int best = 0;
        for (int i = 1; i < DetentBars.Count; i++)
        {
            if (Math.Abs(DetentBars[i] - bars) < Math.Abs(DetentBars[best] - bars))
                best = i;
        }
        return best / (double)(DetentBars.Count - 1);
    }
}
