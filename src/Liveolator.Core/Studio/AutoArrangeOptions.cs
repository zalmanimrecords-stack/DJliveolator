namespace Liveolator.Core.Studio;

/// <summary>
/// Layout options for <see cref="HarmonicAutoArranger"/>: how the harmonically-ordered tracks are
/// placed onto the STUDIO timeline. The harmonic ordering itself is governed separately by
/// <see cref="Liveolator.Core.Playlist.HarmonicSetOptions"/>.
/// </summary>
/// <param name="OverlapSeconds">Crossfade length between consecutive clips: each clip starts this
/// many seconds before the previous one ends, and the pair is fade-matched over the overlap.</param>
/// <param name="StartDeckSlot">Deck lane (0 or 1) the first clip lands on; clips then alternate
/// 0/1 so adjacent tracks play on different decks (DJ-style back-to-back).</param>
/// <param name="ProjectName">Name for the produced <see cref="StudioProject"/>.</param>
public sealed record AutoArrangeOptions(
    double OverlapSeconds = 8.0,
    int StartDeckSlot = 0,
    string ProjectName = "Harmonic Arrangement")
{
    /// <summary>Validates the layout request, throwing for nonsensical values.</summary>
    public void Validate()
    {
        if (OverlapSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(OverlapSeconds), OverlapSeconds, "Overlap cannot be negative.");
        if (StartDeckSlot is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(StartDeckSlot), StartDeckSlot, "Start deck slot must be 0 or 1 (clips alternate between the two).");
    }
}
