namespace Liveolator.Core.Skins;

/// <summary>The outcome of validating a <see cref="ControlSkinFile"/>: valid, or invalid with a reason.</summary>
public sealed record ControlSkinValidation(bool IsValid, string? Error)
{
    public static ControlSkinValidation Ok { get; } = new(true, null);
    public static ControlSkinValidation Fail(string error) => new(false, error);
}

/// <summary>
/// Pure structural validation of a <see cref="ControlSkinFile"/> (doc 30) before it is written or rendered:
/// a name, a known control kind, a required accent colour, and well-formed (<c>#RRGGBB</c>/<c>#AARRGGBB</c>)
/// optional colours. Cheap and UI-free — the actual rendering happens in the app.
/// </summary>
public static class ControlSkinValidator
{
    public static ControlSkinValidation Validate(ControlSkinFile? file)
    {
        if (file is null)
            return ControlSkinValidation.Fail("Skin is null.");
        if (string.IsNullOrWhiteSpace(file.Name))
            return ControlSkinValidation.Fail("Skin name is required.");
        if (!ControlSkinKind.IsKnown(file.Kind))
            return ControlSkinValidation.Fail(
                $"Skin kind must be one of: {string.Join(", ", ControlSkinKind.All)} (found '{file.Kind}').");

        if (string.IsNullOrWhiteSpace(file.Accent))
            return ControlSkinValidation.Fail("Skin accent colour is required.");

        foreach ((string label, string? value) in new[]
        {
            ("accent", (string?)file.Accent),
            ("track", file.Track),
            ("pointer", file.Pointer),
            ("body", file.Body),
        })
        {
            if (value is not null && !IsColor(value))
                return ControlSkinValidation.Fail($"Skin {label} colour '{value}' must be #RRGGBB or #AARRGGBB.");
        }

        return ControlSkinValidation.Ok;
    }

    private static bool IsColor(string value)
    {
        if (value.Length is not (7 or 9) || value[0] != '#')
            return false;
        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
                return false;
        }
        return true;
    }
}
