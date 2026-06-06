namespace Liveolator.Core.Analysis.Key;

/// <summary>
/// Camelot-wheel encoding and harmonic-mixing rules (doc 03 / doc 11). Each key maps to a
/// number 1–12 plus a letter: 'B' = major (outer ring), 'A' = minor (inner ring).
/// Indexed by tonic pitch class (0 = C … 11 = B). E.g. C Major = 8B, A Minor = 8A.
/// </summary>
public static class Camelot
{
    // Camelot number per tonic pitch class.
    private static readonly int[] MajorNumber = { 8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1 };
    private static readonly int[] MinorNumber = { 5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10 };

    /// <summary>Camelot code (e.g. "8B") for a tonic pitch class and mode.</summary>
    public static string Code(int tonic, KeyMode mode)
    {
        int pc = ((tonic % 12) + 12) % 12;
        return mode == KeyMode.Major ? $"{MajorNumber[pc]}B" : $"{MinorNumber[pc]}A";
    }

    /// <summary>
    /// True when <paramref name="other"/> is a harmonically compatible mix from
    /// <paramref name="seed"/>: same code, ±1 number on the same letter (adjacent), or same
    /// number with the letter switched (relative major/minor).
    /// </summary>
    public static bool IsCompatible(string seed, string other)
    {
        if (!TryParse(seed, out int n1, out char l1) || !TryParse(other, out int n2, out char l2))
            return false;

        if (n1 == n2 && l1 == l2) return true;            // same key
        if (n1 == n2 && l1 != l2) return true;            // relative major/minor
        if (l1 == l2)                                     // adjacent on same ring (wraps 12↔1)
        {
            int diff = Math.Abs(n1 - n2);
            return diff == 1 || diff == 11;
        }
        return false;
    }

    /// <summary>
    /// A monotonic sort index for a Camelot code, ordering around the wheel: number ascending,
    /// then 'A' (minor) before 'B' (major) within a number. A null/blank/invalid code sorts last
    /// (<see cref="int.MaxValue"/>), so keyless tracks fall to the bottom of a key sort.
    /// </summary>
    public static int SortIndex(string? code)
        => code is not null && TryParse(code, out int number, out char letter)
            ? (number * 2) + (letter == 'B' ? 1 : 0)
            : int.MaxValue;

    private static bool TryParse(string code, out int number, out char letter)
    {
        number = 0;
        letter = '\0';
        if (string.IsNullOrEmpty(code) || code.Length < 2)
            return false;

        letter = char.ToUpperInvariant(code[^1]);
        if (letter is not ('A' or 'B'))
            return false;

        if (!int.TryParse(code.AsSpan(0, code.Length - 1), out number))
            return false;

        return number is >= 1 and <= 12;
    }
}
