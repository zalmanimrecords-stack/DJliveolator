namespace Liveolator.Core.Recording;

/// <summary>
/// Seam over capturing the live master mix to a file (roadmap X2). The concrete implementation in
/// <c>Liveolator.Audio</c> taps the post-limiter master (the exact signal the house hears) and writes a
/// clean WAV without affecting playback; Core depends only on this interface so the recording action
/// handler unit-tests with a fake. A no-op implementation is registered when no realtime engine is up,
/// so the <see cref="Liveolator.Core.Actions.PerformanceActionKind.MasterRecordToggle"/> kind is always
/// owned (the dispatcher reports it as unavailable rather than throwing).
/// </summary>
public interface IMasterRecorder
{
    /// <summary>
    /// True when this recorder can capture right now (a realtime master tap exists). False on a host
    /// without realtime audio; callers should disable the REC control rather than error.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>True while a capture is in progress (between a successful <see cref="Start"/> and
    /// <see cref="Stop"/>, or an internal stop on an IO failure).</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Begin capturing the master mix to <paramref name="path"/>. Returns true if recording started.
    /// A no-op returning false when <see cref="IsAvailable"/> is false or a capture is already running.
    /// Implementations must surface failures via their own logging and never throw to the caller (a
    /// recording must not crash a live performance).
    /// </summary>
    bool Start(string path);

    /// <summary>
    /// Finish the current capture and finalize the file. Idempotent: stopping when not recording is a
    /// no-op. Like <see cref="Start"/>, never throws to the caller.
    /// </summary>
    void Stop();
}
