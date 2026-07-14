using Liveolator.Core.Analysis.Key;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Resolves a raw key string from any source app into a <see cref="MusicalKey"/>. DJ apps export keys in
/// three notations; this tries each in turn:
/// <list type="number">
///   <item>Camelot (e.g. "8A", "12B") — via <see cref="Camelot.TryToMusicalKey"/>.</item>
///   <item>Classical (e.g. "Am", "F#m", "C", "Db major") — via <see cref="KeyName.TryParse"/>.</item>
///   <item>Open Key (e.g. "1m", "12d") — Mixed-In-Key's wheel; converted to Camelot here.</item>
/// </list>
/// Returns null for a null/blank/unrecognized notation (the track is simply imported keyless).
/// </summary>
public static class ImportKeyParser
{
    public static MusicalKey? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string s = raw.Trim();
        if (Camelot.TryToMusicalKey(s, out MusicalKey? camelot))
            return camelot;
        if (KeyName.TryParse(s, out MusicalKey? classical))
            return classical;
        if (TryOpenKey(s, out MusicalKey? openKey))
            return openKey;
        return null;
    }

    // Open Key notation: 1d..12d (major, 'd' = dur) / 1m..12m (minor). It is the Camelot wheel rotated by
    // 7 — Open Key 1d = C major = Camelot 8B — so camelotNumber = ((open - 1 + 7) % 12) + 1, with d→B, m→A.
    private static bool TryOpenKey(string s, out MusicalKey? key)
    {
        key = null;
        if (s.Length < 2)
            return false;

        char suffix = char.ToLowerInvariant(s[^1]);
        if (suffix is not ('d' or 'm'))
            return false;
        if (!int.TryParse(s.AsSpan(0, s.Length - 1), out int open) || open is < 1 or > 12)
            return false;

        int camelotNumber = ((open - 1 + 7) % 12) + 1;
        char camelotLetter = suffix == 'd' ? 'B' : 'A';
        return Camelot.TryToMusicalKey($"{camelotNumber}{camelotLetter}", out key);
    }
}
