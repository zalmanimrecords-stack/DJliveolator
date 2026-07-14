namespace Liveolator.Mcp.Tools;

/// <summary>Describes the harmonic relationship between two Camelot codes for agent-readable output.</summary>
internal static class CamelotRelationship
{
    public static string Describe(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return "same key";
        // Same number, different letter (e.g. 8A↔8B) → relative major/minor.
        if (from.Length >= 2 && to.Length >= 2
            && from.AsSpan(0, from.Length - 1).SequenceEqual(to.AsSpan(0, to.Length - 1)))
            return "relative major/minor";
        return "adjacent key";
    }

    /// <summary>All 24 Camelot codes (1A–12A, 1B–12B).</summary>
    public static IEnumerable<string> AllCodes()
    {
        for (int n = 1; n <= 12; n++)
        {
            yield return $"{n}A";
            yield return $"{n}B";
        }
    }
}
