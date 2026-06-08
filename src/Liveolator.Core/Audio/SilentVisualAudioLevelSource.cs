namespace Liveolator.Core.Audio;

/// <summary>
/// An <see cref="IVisualAudioLevelSource"/> that always reports silence. The headless fallback (doc 26):
/// when no realtime audio engine is up there is no master mix to meter, so the visuals still render and
/// a reactive add-on simply rests at its floor instead of the engine taking an optional dependency.
/// </summary>
public sealed class SilentVisualAudioLevelSource : IVisualAudioLevelSource
{
    /// <inheritdoc />
    public VisualAudioLevel Current => VisualAudioLevel.Silent;
}
