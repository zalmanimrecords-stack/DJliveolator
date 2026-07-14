namespace Liveolator.Core.Autopilot;

/// <summary>
/// Runs an unattended show from rules, emitting <c>PerformanceAction</c>s through the same
/// dispatcher a human uses (doc 04) — so it gets engine integration for free — while honoring
/// instant manual override (doc 10).
/// </summary>
public interface IAutopilotEngine
{
    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>True while a manual override is suspending rule evaluation.</summary>
    bool IsSuspended { get; }

    /// <summary>Starts the show with the given rule set (resets override and scene history).</summary>
    void Start(AutopilotRuleSet ruleSet);

    /// <summary>Hard-stops the show (the master AUTOPILOT toggle).</summary>
    void Stop();

    /// <summary>Evaluates the rules against one tick and emits any due actions.</summary>
    void Tick(AutopilotTickContext context);

    /// <summary>Notifies that a human action occurred, triggering the override policy.</summary>
    void OnManualAction();
}
