namespace Liveolator.Core.Automix;

/// <summary>
/// Where the auto-mix engine is in its lifecycle. Engaging is IMMEDIATE (doc 11, owner direction):
/// the incoming deck is synced + started on the spot and the slow blend begins right away — sync
/// convergence happens during the quiet start of the curve, not as a gate before it. The invariant
/// stands: no auto-mix failure may interrupt the music that is already playing.
/// </summary>
public enum AutomixPhase
{
    /// <summary>No transition running.</summary>
    Idle,

    /// <summary>Incoming deck launched and syncing; the style profile is driving the mixer.</summary>
    Transitioning,
}
