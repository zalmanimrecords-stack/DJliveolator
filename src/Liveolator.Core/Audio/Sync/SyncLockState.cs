namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// A synced deck's beat-lock state, surfaced to the SYNC button and waveform UI (doc 11 / 12). Mirrors
/// the states professional DJ software shows: not engaged, pulling into lock, locked, or recovering
/// from a slip. The ordinal is carried on the action-feedback <c>Value</c> so the UI can render the
/// right colour/label without a second feedback channel.
/// </summary>
public enum SyncLockState
{
    /// <summary>Sync is not engaged on this deck.</summary>
    Off = 0,

    /// <summary>Engaged and the correction loop is actively pulling the deck toward phase lock.</summary>
    Active = 1,

    /// <summary>Engaged and within the lock tolerance — the deck is beat-locked to the master.</summary>
    Locked = 2,

    /// <summary>Engaged but the phase has slipped past the re-snap threshold; a one-shot beat-snap is recovering it.</summary>
    Drifting = 3,
}
