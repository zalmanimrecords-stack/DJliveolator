namespace Liveolator.Core.Audio;

/// <summary>
/// Maps a sound card's output channels into selectable stereo <b>pairs</b> (doc 01/12): pair 0 = the
/// card's outputs 1/2, pair 1 = 3/4, and so on. This is the single source of truth shared by the
/// Settings picker (which lists pairs for the chosen device) and the BASS backend (which turns a pair
/// index into a speaker-assignment flag), so the UI label and the audio routing can never disagree.
/// Pure data + math — no native code — so it unit-tests with no audio hardware.
/// </summary>
/// <remarks>
/// Capped at <see cref="MaxPairs"/> because BASS speaker assignment addresses at most four front/rear/
/// centre-LFE/rear2 stereo pairs (8 channels). A device reporting more channels still offers four pairs;
/// a mono/unknown device offers the single front pair so the picker always has at least one entry.
/// </remarks>
public static class OutputChannelPair
{
    /// <summary>The most output pairs offered — four stereo pairs = BASS's eight addressable speakers.</summary>
    public const int MaxPairs = 4;

    /// <summary>The highest valid 0-based pair index (<see cref="MaxPairs"/> - 1).</summary>
    public const int MaxPairIndex = MaxPairs - 1;

    /// <summary>
    /// How many stereo pairs a device with <paramref name="outputChannelCount"/> channels exposes:
    /// channels / 2, but always at least one (so a picker is never empty) and never more than
    /// <see cref="MaxPairs"/>.
    /// </summary>
    public static int PairCount(int outputChannelCount)
        => Math.Clamp(outputChannelCount / 2, 1, MaxPairs);

    /// <summary>The 1-based first channel of a pair index: 0 → 1, 1 → 3, 2 → 5, 3 → 7.</summary>
    public static int FirstChannel(int pairIndex) => pairIndex * 2 + 1;

    /// <summary>Human-readable picker label for a pair index, e.g. "Outputs 3/4".</summary>
    public static string Label(int pairIndex)
        => $"Outputs {FirstChannel(pairIndex)}/{FirstChannel(pairIndex) + 1}";

    /// <summary>
    /// Clamps a (possibly stale / hand-edited) pair index to a device's valid range — so a saved
    /// "outputs 3/4" choice falls back to "1/2" when reloaded against a card that only has two outputs.
    /// </summary>
    public static int Clamp(int pairIndex, int outputChannelCount)
        => Math.Clamp(pairIndex, 0, PairCount(outputChannelCount) - 1);
}
