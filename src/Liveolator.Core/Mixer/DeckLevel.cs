namespace Liveolator.Core.Mixer;

/// <summary>One deck channel's latest post-processing signal level, normalized to 0..1.</summary>
public readonly record struct DeckLevel(double Peak, double Rms)
{
    public static DeckLevel Silent => new(0, 0);
}
