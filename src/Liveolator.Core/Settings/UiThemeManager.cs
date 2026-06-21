using System.Globalization;

namespace Liveolator.Core.Settings;

public sealed class UiThemeManager : IUiThemeManager
{
    private static readonly IReadOnlySet<string> ColorTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "BgColor", "S1Color", "S2Color", "S3Color", "S4Color", "HairColor", "TextColor",
        "DimColor", "FaintColor", "AccentColor", "AccentLightColor", "AccentDarkColor",
        "AccentWellColor", "AccentInkColor", "RedColor", "GreenColor", "AmberColor",
        "VioletColor", "MidiActiveColor", "WaveformColor", "KickColor",
        "WaveMidColor", "WaveHighColor",
        // Optional per-control colours (doc 30): let a theme style the knobs/faders directly (e.g. a
        // vintage cream cap + amber arc) independently of the surface/text tokens. Override the control
        // brush resources the Knob/Fader styles bind to. An active control skin still wins over these.
        "KnobArcColor", "KnobTrackColor", "KnobCapColor", "KnobPointerColor",
        "FaderFillColor", "FaderTrackColor", "FaderThumbColor",
    };

    // Image tokens carry an asset reference (avares:// or file path), not a colour — e.g. a window
    // background texture (doc 30). Validated only for shape; the App resolves it to an ImageBrush.
    private static readonly IReadOnlySet<string> ImageTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "BackgroundImage",
    };

    private static readonly IReadOnlySet<string> NumericTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "PanelRadius", "ControlRadius", "ControlHeight", "ModuleSpacing",
    };

    private static readonly IReadOnlySet<string> FontTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "UiFontFamily", "MonoFontFamily",
    };

    // Enum tokens pick one of a fixed set of named looks rather than a colour/number. KnobStyle lets a
    // theme swap the knob's drawn shape (e.g. the Retro Sci-Fi chicken-head amp knob) without affecting
    // other themes. The App maps the value to a render variant; an absent token = the default Rotary look.
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EnumTokens =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["KnobStyle"] = new HashSet<string>(StringComparer.Ordinal) { "Rotary", "ScallopedDial" },
        };

    private readonly object _gate = new();
    private readonly Dictionary<string, UiThemeDefinition[]> _packages = new(StringComparer.Ordinal);
    private UiThemeDefinition[] _themes = Array.Empty<UiThemeDefinition>();

    public IReadOnlyList<UiThemeDefinition> AvailableThemes => Volatile.Read(ref _themes);

    public UiThemeValidationResult Validate(UiThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(theme.Id))
            errors.Add("Theme id is required.");
        if (string.IsNullOrWhiteSpace(theme.Name))
            errors.Add("Theme name is required.");

        foreach ((string key, string value) in theme.Tokens)
        {
            if (ColorTokens.Contains(key))
            {
                if (!IsColor(value))
                    errors.Add($"Token '{key}' must be #RRGGBB or #AARRGGBB.");
            }
            else if (NumericTokens.Contains(key))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    || number is < 0 or > 128)
                    errors.Add($"Token '{key}' must be a number from 0 to 128.");
            }
            else if (FontTokens.Contains(key))
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
                    errors.Add($"Token '{key}' contains an invalid font family.");
            }
            else if (ImageTokens.Contains(key))
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl))
                    errors.Add($"Token '{key}' must be a non-empty asset reference (avares:// or a file path).");
            }
            else if (EnumTokens.TryGetValue(key, out IReadOnlySet<string>? allowed))
            {
                if (!allowed.Contains(value))
                    errors.Add($"Token '{key}' must be one of: {string.Join(", ", allowed)}.");
            }
            else
            {
                errors.Add($"Token '{key}' is not allowed.");
            }
        }

        return new UiThemeValidationResult(errors.Count == 0, errors.Count == 0 ? theme : null, errors);
    }

    public bool TryGet(string id, out UiThemeDefinition theme)
    {
        theme = AvailableThemes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))!;
        return theme is not null;
    }

    public void ReplacePackage(string packageId, IEnumerable<UiThemeDefinition> themes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(themes);
        UiThemeDefinition[] valid = themes.Select(theme =>
        {
            UiThemeValidationResult result = Validate(theme);
            if (!result.IsValid)
                throw new ArgumentException(string.Join(" ", result.Errors), nameof(themes));
            return theme;
        }).ToArray();

        lock (_gate)
        {
            _packages[packageId] = valid;
            Publish();
        }
    }

    public void RemovePackage(string packageId)
    {
        lock (_gate)
        {
            _packages.Remove(packageId);
            Publish();
        }
    }

    private void Publish()
    {
        UiThemeDefinition[] themes = _packages.Values.SelectMany(t => t).ToArray();
        if (themes.GroupBy(t => t.Id, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidOperationException("A UI theme id is registered more than once.");
        Volatile.Write(ref _themes, themes);
    }

    private static bool IsColor(string value)
    {
        if (value.Length is not (7 or 9) || value[0] != '#')
            return false;
        return value.AsSpan(1).ToString().All(Uri.IsHexDigit);
    }
}
