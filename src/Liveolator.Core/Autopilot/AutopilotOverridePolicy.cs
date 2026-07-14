namespace Liveolator.Core.Autopilot;

/// <summary>
/// How manual override behaves. Default is the forgiving one: suspend briefly, then auto-resume so
/// the performer can grab control without the show getting stuck off (doc 10).
/// </summary>
/// <param name="Mode">Auto-resume (default) or pause-until-reenabled.</param>
/// <param name="ResumeAfterBars">Bars to stay suspended after the last manual action (AutoResume).</param>
public sealed record AutopilotOverridePolicy(
    OverrideMode Mode = OverrideMode.AutoResume,
    int ResumeAfterBars = 2)
{
    /// <summary>The forgiving default: auto-resume after 2 bars.</summary>
    public static AutopilotOverridePolicy Default { get; } = new();
}
