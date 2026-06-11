namespace Liveolator.Core.Automix;

/// <summary>
/// Where the auto-mix engine is in its lifecycle. The audible blend exists only in
/// <see cref="Transitioning"/>; every earlier phase is silent preparation that can abort without the
/// floor ever hearing a problem (the supreme invariant: no auto-mix failure may interrupt the music
/// that is already playing).
/// </summary>
public enum AutomixPhase
{
    /// <summary>No transition armed.</summary>
    Idle,

    /// <summary>Plan accepted; incoming deck synced/seeked, waiting for the leader's next downbeat to start it.</summary>
    Arming,

    /// <summary>Incoming deck rolling silently; waiting for a confirmed beat lock (or timeout → abort).</summary>
    Syncing,

    /// <summary>Beat-locked and bar-anchored; the style profile is driving the mixer.</summary>
    Transitioning,
}
