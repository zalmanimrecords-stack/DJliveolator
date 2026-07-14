namespace Liveolator.Core.Actions;

/// <summary>
/// Raised when the feedback state of one action (identified by kind and slot) changes, so
/// subscribers refresh exactly the affected LED or indicator.
/// </summary>
/// <param name="Kind">The action whose state changed.</param>
/// <param name="Slot">The target index that changed (0 when the kind has no slots).</param>
/// <param name="State">The new feedback state.</param>
public sealed record ActionFeedbackChanged(PerformanceActionKind Kind, int Slot, ActionFeedbackState State);
