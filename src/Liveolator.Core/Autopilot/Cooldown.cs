namespace Liveolator.Core.Autopilot;

/// <summary>
/// Minimum bars between two firings of the same rule, preventing flicker (doc 10). 0 = no cooldown.
/// </summary>
public sealed record Cooldown(int Bars)
{
    /// <summary>No cooldown.</summary>
    public static Cooldown None { get; } = new(0);

    /// <summary>Validates the cooldown is non-negative.</summary>
    public void Validate()
    {
        if (Bars < 0)
            throw new ArgumentOutOfRangeException(nameof(Bars), Bars, "Cooldown bars cannot be negative.");
    }
}
