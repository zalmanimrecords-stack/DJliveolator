using Liveolator.Core.Actions;

namespace Liveolator.Core.Autopilot;

/// <summary>
/// One autopilot rule: when it fires, an optional gate, the action it emits through the dispatcher
/// (like a human, doc 04), and how long before it can fire again (doc 10).
/// </summary>
/// <param name="Name">Human-facing name, used in logs/post-mortems.</param>
/// <param name="Trigger">When to evaluate.</param>
/// <param name="Condition">Optional gate; use <see cref="RuleCondition.None"/> for no gate.</param>
/// <param name="Action">The action to emit (scene-selecting actions draw from the scene pool).</param>
/// <param name="Cooldown">Minimum bars between firings.</param>
public sealed record AutopilotRule(
    string Name,
    RuleTrigger Trigger,
    RuleCondition Condition,
    PerformanceAction Action,
    Cooldown Cooldown);
