namespace Liveolator.Core.Audio;

/// <summary>Smoothed frequency-band energy for audio-reactive visual generators.</summary>
public sealed record VisualAudioBands(double Bass, double LowMid, double Mid, double High)
{
    public static VisualAudioBands Silent { get; } = new(0, 0, 0, 0);
}

public interface IVisualAudioBandsSource
{
    VisualAudioBands CurrentBands { get; }
}
