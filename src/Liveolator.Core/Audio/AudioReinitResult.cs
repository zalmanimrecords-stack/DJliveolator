namespace Liveolator.Core.Audio;

/// <summary>
/// The outcome of an <see cref="AudioReinitCoordinator.Apply"/> call, so the caller (the Settings UI)
/// can surface a clear status to the performer (global standard #26 — no silent failures).
/// </summary>
public enum AudioReinitResult
{
    /// <summary>The output device + buffer were unchanged; the engine was not touched.</summary>
    NoChange,

    /// <summary>The engine was re-opened on the new device / buffer.</summary>
    Reinitialized,

    /// <summary>The re-open failed; the engine was restored to the previously working settings.</summary>
    RolledBack,
}
