using System.Globalization;

namespace Liveolator.App.Features.Shared;

/// <summary>
/// Parses and range-validates a hand-typed BPM. A musical floor/ceiling stops a fat-fingered value
/// (e.g. "9999") from being saved as a manual override and then corrupting a deck's Sync reference
/// (doc 31 L4). Pure so the validation is unit-tested without the editor window.
/// </summary>
internal static class BpmInput
{
    /// <summary>Lowest accepted musical tempo.</summary>
    public const double Min = 40.0;

    /// <summary>Highest accepted musical tempo.</summary>
    public const double Max = 300.0;

    /// <summary>True when <paramref name="text"/> parses to a tempo within [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public static bool TryParse(string? text, out double bpm)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm)
           && bpm >= Min && bpm <= Max;
}
