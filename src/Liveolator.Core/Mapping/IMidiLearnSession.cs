using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Captures the next inbound message after the user arms an action, infers a binding from it, and
/// raises <see cref="Learned"/>. The inferred input mode is always user-overridable (doc 05).
/// </summary>
public interface IMidiLearnSession
{
    /// <summary>True between <see cref="Begin"/> and the next captured message (or <see cref="Cancel"/>).</summary>
    bool IsArmed { get; }

    /// <summary>Arms capture for <paramref name="action"/> at <paramref name="slot"/>.</summary>
    void Begin(
        PerformanceActionKind action,
        int slot = 0,
        string? argument = null,
        ActionInputMode? preferredInputMode = null);

    /// <summary>Feeds an inbound message; when armed, the first one is captured into a binding.</summary>
    void Observe(MidiMessage message);

    /// <summary>Disarms without producing a binding.</summary>
    void Cancel();

    /// <summary>Raised once a binding has been inferred from a captured message.</summary>
    event EventHandler<ControllerBinding>? Learned;
}
