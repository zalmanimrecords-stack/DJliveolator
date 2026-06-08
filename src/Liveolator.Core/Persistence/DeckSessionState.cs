namespace Liveolator.Core.Persistence;

/// <summary>The track restored into one deck slot when the application starts.</summary>
public sealed record DeckSessionState(
    int Slot,
    string TrackPath,
    double Bpm = 0,
    double FirstBeatSeconds = 0);
