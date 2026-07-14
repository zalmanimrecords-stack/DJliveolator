namespace Liveolator.Core.Audio;

/// <summary>
/// An immutable snapshot of the live audio level the visuals react to (doc 26 — the audio-reactive
/// seam for the compositor). Published by swapping the whole record so a render thread reads a
/// consistent picture without locking, mirroring <see cref="Beat.BeatClockState"/>.
/// </summary>
/// <param name="Rms">Block RMS of the latest analysis frame, normalized 0..1.</param>
/// <param name="Peak">Block peak (max abs sample) of the latest frame, normalized 0..1.</param>
/// <param name="Vu">
/// VU-ballistics value (fast attack / slow release) smoothed across frames — the "needle" a meter
/// add-on swings. Normalized 0..1.
/// </param>
public sealed record VisualAudioLevel(double Rms, double Peak, double Vu)
{
    /// <summary>Silence: the resting level before any audio (and the headless fallback).</summary>
    public static VisualAudioLevel Silent { get; } = new(0, 0, 0);
}
