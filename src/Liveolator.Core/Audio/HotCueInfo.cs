using System.Globalization;

namespace Liveolator.Core.Audio;

/// <summary>
/// A deck hot-cue's display state for one pad (doc 11): whether the slot holds a cue, and — when it does —
/// the optional performer <see cref="Label"/>, the optional 0xRRGGBB pad <see cref="Color"/>, and whether
/// it is still an unconfirmed auto-placed suggestion (<see cref="IsAuto"/>). Lets a deck pad show the cue's
/// name and color (e.g. a red "Drop"), not just a lit number, and mark suggestions distinctly.
/// </summary>
public readonly record struct HotCueInfo(bool IsSet, string? Label = null, int? Color = null, bool IsAuto = false)
{
    /// <summary>The empty/unset slot.</summary>
    public static readonly HotCueInfo Unset = new(IsSet: false);
}

/// <summary>
/// Encodes a deck hot-cue's index + <see cref="HotCueInfo"/> into the single <c>Argument</c> string of a
/// <c>DeckHotCue</c> feedback (the action feedback bus carries no per-cue struct), and decodes it back on
/// the UI side. The wire form is <c>index|auto|color|label</c>; a bare integer (the historical form, and
/// any feedback that carries only the index) decodes to a set cue with no metadata, so older raise sites
/// keep working unchanged.
/// </summary>
public static class HotCueFeedback
{
    private const char Separator = '|';

    /// <summary>Pack the cue index and its info into a feedback <c>Argument</c> string.</summary>
    public static string Encode(int index, HotCueInfo info)
    {
        string color = info.Color is { } c ? c.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return string.Join(
            Separator,
            index.ToString(CultureInfo.InvariantCulture),
            info.IsAuto ? "1" : "0",
            color,
            info.Label ?? string.Empty);
    }

    /// <summary>
    /// Parse a feedback <c>Argument</c> back into the cue index and its info. Returns false (and a null
    /// index) when the string is empty or its leading field is not an integer, so a malformed echo is
    /// ignored rather than throwing. A bare integer yields a set cue with no metadata.
    /// </summary>
    public static bool TryDecode(string? argument, out int index, out HotCueInfo info)
    {
        index = 0;
        info = HotCueInfo.Unset;
        if (string.IsNullOrEmpty(argument))
            return false;

        string[] parts = argument.Split(Separator, 4);
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            return false;

        if (parts.Length == 1)
        {
            // Bare index (historical / index-only feedback): a set cue with no extra metadata.
            info = new HotCueInfo(IsSet: true);
            return true;
        }

        bool isAuto = parts[1] == "1";
        int? color = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rgb)
            ? rgb
            : null;
        string? label = string.IsNullOrEmpty(parts[3]) ? null : parts[3];
        info = new HotCueInfo(IsSet: true, label, color, isAuto);
        return true;
    }
}
