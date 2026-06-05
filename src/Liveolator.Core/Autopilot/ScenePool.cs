namespace Liveolator.Core.Autopilot;

/// <summary>
/// The curated set of scenes autopilot may pick from — controlled randomness, never the full scene
/// set — with a per-scene cooldown so a scene cannot repeat within N bars (doc 10).
/// </summary>
/// <param name="SceneNames">Scene names drawn from a <c>VisualBank</c> (doc 08).</param>
/// <param name="CooldownBars">A chosen scene cannot be chosen again within this many bars.</param>
public sealed record ScenePool(IReadOnlyList<string> SceneNames, int CooldownBars)
{
    /// <summary>An empty pool (autopilot emits scene actions unchanged).</summary>
    public static ScenePool Empty { get; } = new(Array.Empty<string>(), 0);
}
