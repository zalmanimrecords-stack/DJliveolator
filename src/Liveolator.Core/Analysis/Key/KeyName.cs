namespace Liveolator.Core.Analysis.Key;

/// <summary>
/// Parses a human-readable musical-key name (e.g. "Am", "F#m", "C", "Db major", "A minor") into a
/// <see cref="MusicalKey"/>, deriving its Camelot code via <see cref="Camelot"/>. Online providers
/// report keys as names rather than Camelot codes (e.g. GetSongBPM's <c>key_of</c> = "Am"), so this is
/// the bridge that lets such a key actually reach the catalog (doc 27 B7). Accepts sharps (# / ♯) and
/// flats (b / ♭) including enharmonics; the mode is minor when the suffix is m / min / minor / "-"
/// (any case), otherwise major.
/// </summary>
public static class KeyName
{
    // Each spelling (sharps and flats, plus the four enharmonic edge spellings) → pitch class 0..11 (0 = C).
    private static readonly IReadOnlyDictionary<string, int> PitchClasses =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["C"] = 0, ["B#"] = 0,
            ["C#"] = 1, ["Db"] = 1,
            ["D"] = 2,
            ["D#"] = 3, ["Eb"] = 3,
            ["E"] = 4, ["Fb"] = 4,
            ["F"] = 5, ["E#"] = 5,
            ["F#"] = 6, ["Gb"] = 6,
            ["G"] = 7,
            ["G#"] = 8, ["Ab"] = 8,
            ["A"] = 9,
            ["A#"] = 10, ["Bb"] = 10,
            ["B"] = 11, ["Cb"] = 11,
        };

    /// <summary>
    /// Parses <paramref name="name"/> into a <see cref="MusicalKey"/> (confidence 1.0, matching
    /// <see cref="Camelot.TryToMusicalKey"/>'s convention for an externally-provided key). Returns
    /// false for a null/blank/unrecognized name.
    /// </summary>
    public static bool TryParse(string? name, out MusicalKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string s = name.Trim().Replace('♯', '#').Replace('♭', 'b'); // ♯/♭ → ASCII
        if (s.Length == 0 || !"ABCDEFGabcdefg".Contains(s[0]))
            return false;

        // Pitch = the letter plus an optional accidental ('#' or lowercase 'b'); uppercase 'B' is the
        // note name, never a flat, so it is not consumed as an accidental.
        int rest = 1;
        string pitch = s[..1];
        if (s.Length > 1 && (s[1] == '#' || s[1] == 'b'))
        {
            pitch = s[..2];
            rest = 2;
        }

        if (!PitchClasses.TryGetValue(pitch, out int tonic))
            return false;

        string suffix = s[rest..].Trim().ToLowerInvariant();
        KeyMode mode;
        if (suffix.Length == 0 || suffix.StartsWith("maj"))
            mode = KeyMode.Major;
        else if (suffix == "-" || suffix.StartsWith("m"))   // m / min / minor / "-"
            mode = KeyMode.Minor;
        else
            return false; // a trailing token we don't understand → don't guess

        key = new MusicalKey(tonic, mode, Camelot.Code(tonic, mode), Confidence: 1.0);
        return true;
    }
}
