namespace Liveolator.Core.Persistence;

/// <summary>The track restored into one deck slot when the application starts.</summary>
/// <param name="DownbeatSeconds">A manually-set downbeat (the bar's "one") for the track on this deck, so a
/// DJ's grid edit survives a restart. 0 = none set (the analyzed downbeat is re-derived on load). Appended
/// with a default so older saved sessions deserialize unchanged.</param>
public sealed record DeckSessionState(
    int Slot,
    string TrackPath,
    double Bpm = 0,
    double FirstBeatSeconds = 0,
    double DownbeatSeconds = 0);
