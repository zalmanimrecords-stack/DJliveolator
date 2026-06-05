using Liveolator.Core.Beat;

namespace Liveolator.Core.Autopilot;

/// <summary>
/// The inputs a rule can react to on one tick: the shared beat-clock snapshot plus the audio energy
/// and the current track position (doc 10). Supplied by the host each beat/frame.
/// </summary>
/// <param name="State">The latest beat-clock state (beat/bar counts, confidence, IsBeat/IsDownbeat).</param>
/// <param name="Energy">Current audio energy, 0..1.</param>
/// <param name="TrackPosition">Position through the current track, 0..1.</param>
public sealed record AutopilotTickContext(BeatClockState State, double Energy, double TrackPosition);
