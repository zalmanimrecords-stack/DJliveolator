namespace Liveolator.Core.Studio;

/// <summary>Whether a clip event begins playback of a clip or ends it.</summary>
public enum StudioClipEventKind
{
    /// <summary>Load + seek to the clip's in-point + start the deck at this timeline position.</summary>
    Start,

    /// <summary>Stop the deck at the clip's out-point.</summary>
    Stop,
}

/// <summary>
/// A clip lifecycle event the live transport executes (Phase 4). Clip transport is modelled as
/// typed Start/Stop events rather than raw actions because there is no idempotent "stop deck"
/// action (only a stateful play/pause toggle), so the transport owns deck lifecycle while the
/// scheduler stays pure. Carries the whole <see cref="StudioClip"/> so the transport has the deck
/// slot, source path, and in-point.
/// </summary>
public sealed record StudioClipEvent(double TimeSeconds, StudioClipEventKind Kind, StudioClip Clip);
