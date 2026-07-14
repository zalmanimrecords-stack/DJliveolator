namespace Liveolator.Core.Visuals;

/// <summary>
/// How a scene/layer reacts to the shared beat clock (doc 03/08): which boundaries pulse effects
/// and how often a clip relaunches. Expressed against the one shared clock, so audio and visuals
/// stay locked by construction.
/// </summary>
/// <param name="PulseOnBeat">Pulse bound effects on every beat.</param>
/// <param name="PulseOnDownbeat">Pulse bound effects on every bar downbeat.</param>
/// <param name="LaunchEveryBars">Relaunch cadence in bars; 0 = no automatic relaunch.</param>
public sealed record BeatBehavior(bool PulseOnBeat, bool PulseOnDownbeat, int LaunchEveryBars)
{
    /// <summary>No beat reactivity and no automatic relaunch.</summary>
    public static BeatBehavior None { get; } = new(PulseOnBeat: false, PulseOnDownbeat: false, LaunchEveryBars: 0);
}
