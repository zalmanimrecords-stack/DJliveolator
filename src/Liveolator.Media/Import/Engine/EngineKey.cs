namespace Liveolator.Media.Import.Engine;

/// <summary>
/// Converts Engine DJ's integer key id (0–23) to a Camelot code. Engine orders keys around the wheel
/// rather than chromatically: 0 = 8B (C major), 1 = 8A (A minor), … 23 = 7A — so the number advances by
/// one Camelot step every two ids and the parity selects major (B) / minor (A). The formula reproduces
/// all three documented anchors (0→8B, 1→8A, 23→7A).
/// </summary>
internal static class EngineKey
{
    public static string? ToCamelot(int key)
    {
        if (key is < 0 or > 23)
            return null;
        int number = ((key / 2 + 7) % 12) + 1;
        char letter = key % 2 == 0 ? 'B' : 'A';
        return $"{number}{letter}";
    }
}
