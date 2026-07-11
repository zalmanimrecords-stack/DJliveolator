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

    /// <summary>User's 0–5 star rating; unrated (0) tracks sort last.</summary>
    Rating,

    /// <summary>When the track was added to the library; never-stamped tracks sort last (for "recently added").</summary>
    DateAdded,

    /// <summary>How many times the track was loaded to a deck (0 = never played).</summary>
    PlayCount,
}
