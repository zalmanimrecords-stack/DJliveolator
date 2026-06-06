namespace Liveolator.Core.Library.Music;

/// <summary>The track attribute a library view orders by. Drives the Libraries sort control.</summary>
public enum TrackSortKey
{
    /// <summary>Display title (the default browse order).</summary>
    Title,

    /// <summary>Detected tempo; tracks without a BPM sort last.</summary>
    Bpm,

    /// <summary>Camelot key, ordered around the wheel (number then A/B); keyless tracks sort last.</summary>
    Key,

    /// <summary>Playing length; tracks without a known duration sort last.</summary>
    Duration,
}
