namespace Liveolator.Core.Autopilot;

/// <summary>How autopilot reacts to a manual action (doc 10).</summary>
public enum OverrideMode
{
    /// <summary>Suspend for a window after the last manual action, then resume automatically.</summary>
    AutoResume,

    /// <summary>Stop until the performer re-enables autopilot.</summary>
    PauseUntilReenabled,
}
