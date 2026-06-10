using System.Globalization;
using System.Text;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// Turns a technical generator <c>EffectId</c> (e.g. <c>liveolator.builtin/vu-meter</c>) into a
/// human-readable name for the LAYERS source picker. Pure presentation — no engine state.
/// </summary>
public static class VisualSourceLabel
{
    // EffectIds whose local part is a placeholder carry no meaning on their own (e.g. the milkdrop
    // package's single "generator"); for these the package's own name reads far nicer to a performer.
    private static readonly HashSet<string> GenericLocalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "generator", "effect", "main", "shader", "default",
    };

    // Tokens that should stay uppercase rather than be title-cased (acronyms common to VJ/DJ effects).
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "vu", "fx", "hd", "rgb", "led", "vj", "dj", "uv",
    };

    /// <summary>
    /// Derives a display name from an <c>EffectId</c> shaped as <c>package.id/local-name</c>. Falls back
    /// to the package's last segment when the local name is a generic placeholder, then humanizes the
    /// chosen token set (separators → spaces, title case, preserved acronyms).
    /// </summary>
    public static string Humanize(string? effectId)
    {
        if (string.IsNullOrWhiteSpace(effectId))
            return string.Empty;

        int slash = effectId.LastIndexOf('/');
        string packagePart = slash >= 0 ? effectId[..slash] : string.Empty;
        string localPart = slash >= 0 ? effectId[(slash + 1)..] : effectId;

        string source = localPart;
        if (string.IsNullOrWhiteSpace(localPart) || GenericLocalNames.Contains(localPart))
        {
            string packageName = LastSegment(packagePart);
            if (!string.IsNullOrWhiteSpace(packageName))
                source = packageName;
        }

        return HumanizeTokens(source);
    }

    private static string LastSegment(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return string.Empty;
        int dot = packageId.LastIndexOf('.');
        return dot >= 0 ? packageId[(dot + 1)..] : packageId;
    }

    private static string HumanizeTokens(string value)
    {
        string[] tokens = value.Split(
            new[] { '-', '_', '.', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (string token in tokens)
        {
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(Acronyms.Contains(token) ? token.ToUpperInvariant() : TitleCase(token));
        }

        return builder.ToString();
    }

    private static string TitleCase(string token)
        => char.ToUpper(token[0], CultureInfo.InvariantCulture) + token[1..].ToLowerInvariant();
}
