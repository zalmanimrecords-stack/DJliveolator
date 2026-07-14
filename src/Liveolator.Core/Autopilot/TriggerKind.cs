namespace Liveolator.Core.Autopilot;

/// <summary>When a rule is evaluated against the beat clock (doc 10).</summary>
public enum TriggerKind
{
    /// <summary>Every N beats (N from <see cref="RuleTrigger.N"/>).</summary>
    EveryNBeats,

    /// <summary>Every N bars.</summary>
    EveryNBars,

    /// <summary>On every bar downbeat.</summary>
    OnDownbeat,

    /// <summary>When the track position reaches N percent (0..100) of the track.</summary>
    OnTrackPosition,
}
