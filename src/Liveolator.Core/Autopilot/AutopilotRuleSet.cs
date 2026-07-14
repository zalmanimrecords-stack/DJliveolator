namespace Liveolator.Core.Autopilot;

/// <summary>
/// A complete show definition: the rules, the curated scene pool, the override policy, and an
/// explicit RNG seed so a show is reproducible/debuggable and deterministic for tests (doc 10/14).
/// Serialized as part of a saved show (doc 13).
/// </summary>
/// <param name="Name">Show/rule-set name.</param>
/// <param name="Rules">The rules, evaluated in order each tick.</param>
/// <param name="ScenePool">Controlled-randomness source for scene-selecting actions.</param>
/// <param name="Seed">Seed for the deterministic scene picker.</param>
/// <param name="OverridePolicy">Manual-override behavior for this show.</param>
public sealed record AutopilotRuleSet(
    string Name,
    IReadOnlyList<AutopilotRule> Rules,
    ScenePool ScenePool,
    int Seed = 0,
    AutopilotOverridePolicy? OverridePolicy = null)
{
    /// <summary>The override policy, or the forgiving default when unset.</summary>
    public AutopilotOverridePolicy Policy => OverridePolicy ?? AutopilotOverridePolicy.Default;
}
