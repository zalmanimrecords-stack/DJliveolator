using System.Globalization;

namespace Liveolator.Core.Audio;

/// <summary>
/// Compact serializer for analyzed kick onset times carried on <see cref="Actions.PerformanceAction.Argument"/>.
/// </summary>
public static class DeckKickOnsetCodec
{
    private const int MaxOnsets = 2048;

    public static string? Encode(IReadOnlyList<double>? kickOnsetsSeconds)
    {
        if (kickOnsetsSeconds is null || kickOnsetsSeconds.Count == 0)
            return null;

        double[] values = kickOnsetsSeconds
            .Where(v => double.IsFinite(v) && v >= 0.0)
            .OrderBy(v => v)
            .Take(MaxOnsets)
            .ToArray();
        if (values.Length == 0)
            return null;

        return string.Join(
            ";",
            values.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));
    }

    public static IReadOnlyList<double> Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return Array.Empty<double>();

        var values = new List<double>();
        foreach (string part in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value)
                && value >= 0.0)
            {
                values.Add(value);
            }
        }

        values.Sort();
        return values;
    }
}
