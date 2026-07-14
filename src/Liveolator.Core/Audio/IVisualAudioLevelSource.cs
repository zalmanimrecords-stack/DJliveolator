namespace Liveolator.Core.Audio;

/// <summary>
/// Read seam exposing the live audio level the visual compositor reacts to (doc 26). The visual engine
/// samples <see cref="Current"/> from its render thread each frame — exactly as it samples
/// <see cref="Beat.IBeatClock.Current"/> — so reactive shaders (a VU meter, a level-driven effect) get
/// the master signal's amplitude. Sampling a level/clock is the engine's read path; it is distinct from
/// the <c>PerformanceAction</c> command path.
/// </summary>
public interface IVisualAudioLevelSource
{
    /// <summary>The latest immutable level snapshot, or <see cref="VisualAudioLevel.Silent"/> if none yet.</summary>
    VisualAudioLevel Current { get; }
}
