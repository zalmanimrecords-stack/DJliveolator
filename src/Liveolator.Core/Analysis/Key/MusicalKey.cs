namespace Liveolator.Core.Analysis.Key;

/// <summary>Diatonic mode — the "scale" of a key.</summary>
public enum KeyMode
{
    Major,
    Minor
}

/// <summary>
/// A detected musical key: tonic pitch class (0 = C … 11 = B), mode (scale), the Camelot
/// harmonic-mixing code, and a 0..1 confidence.
/// </summary>
public sealed record MusicalKey(int Tonic, KeyMode Mode, string Camelot, double Confidence)
{
    private static readonly string[] PitchNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>Human-readable name, e.g. "C Major" / "A Minor".</summary>
    public string Name => $"{PitchNames[((Tonic % 12) + 12) % 12]} {(Mode == KeyMode.Major ? "Major" : "Minor")}";
}
