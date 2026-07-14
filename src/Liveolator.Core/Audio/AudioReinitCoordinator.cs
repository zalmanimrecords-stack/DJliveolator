using Liveolator.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Drives a runtime audio re-init when the user changes the output device or buffer in Settings
/// (doc 12): decides whether a change actually requires re-opening the device, invokes the native
/// <see cref="IAudioEngineReinitializer"/>, and — critically — rolls back to the last working
/// settings if the re-open fails so the app is never left silently without audio (global standards
/// #16/#26). Pure C# (no native): the device decision + rollback logic unit-test against a fake
/// reinitializer.
/// </summary>
/// <remarks>
/// Only the output <b>device</b> and <b>buffer</b> drive a re-open — these are the BASS init-time
/// parameters (<c>BassInitOptions</c>). Other settings (capture source, MIDI) are applied through
/// their own seams and do not re-open the output. Holding the last-applied settings here keeps the
/// rollback target authoritative even across several changes in one session.
/// </remarks>
public sealed class AudioReinitCoordinator
{
    private readonly IAudioEngineReinitializer _reinitializer;
    private readonly ILogger<AudioReinitCoordinator> _logger;
    private readonly object _gate = new();
    private AudioSettings _current;

    /// <param name="reinitializer">The native re-open seam (a fake in tests).</param>
    /// <param name="startupSettings">
    /// The settings the engine was opened with at startup — the initial rollback target and the
    /// baseline a change is compared against. Null = <see cref="AudioSettings.Default"/>.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public AudioReinitCoordinator(
        IAudioEngineReinitializer reinitializer,
        AudioSettings? startupSettings = null,
        ILogger<AudioReinitCoordinator>? logger = null)
    {
        _reinitializer = reinitializer ?? throw new ArgumentNullException(nameof(reinitializer));
        _logger = logger ?? NullLogger<AudioReinitCoordinator>.Instance;
        _current = (startupSettings ?? AudioSettings.Default).Normalized();
    }

    /// <summary>The settings the engine is currently running on (the rollback target).</summary>
    public AudioSettings Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Apply a new settings selection. If the output device / buffer is unchanged this is a no-op and
    /// returns <see cref="AudioReinitResult.NoChange"/> (avoids an audible glitch from a needless
    /// re-open). Otherwise re-opens the device; on success the new settings become the rollback target
    /// (<see cref="AudioReinitResult.Reinitialized"/>); on failure the engine is restored to the prior
    /// working settings and <see cref="AudioReinitResult.RolledBack"/> is returned. A failure is logged,
    /// never swallowed silently.
    /// </summary>
    public AudioReinitResult Apply(AudioSettings? settings)
    {
        AudioSettings next = (settings ?? AudioSettings.Default).Normalized();

        lock (_gate)
        {
            if (!RequiresReopen(_current, next))
                return AudioReinitResult.NoChange;

            AudioSettings previous = _current;
            if (TryReinitialize(next))
            {
                _current = next;
                _logger.LogInformation(
                    "Audio re-initialised on device '{Device}' at {Buffer} ms.",
                    next.OutputDeviceId ?? "(system default)", next.BufferMilliseconds);
                return AudioReinitResult.Reinitialized;
            }

            // Re-open failed: restore the prior working device so the app keeps audio.
            _logger.LogError(
                "Audio re-init to device '{Device}' failed; rolling back to '{Previous}'.",
                next.OutputDeviceId ?? "(system default)", previous.OutputDeviceId ?? "(system default)");

            if (!TryReinitialize(previous))
                _logger.LogError(
                    "Audio rollback to device '{Previous}' also failed; audio may be unavailable.",
                    previous.OutputDeviceId ?? "(system default)");

            // _current stays as `previous` — it remains the authoritative rollback target.
            return AudioReinitResult.RolledBack;
        }
    }

    // A re-open is only needed when an init-time BASS parameter changes: the master device/buffer, OR
    // the headphone-cue routing (its device or either output channel-pair), since those re-shape the
    // device + speaker assignment the backend opens. Caller holds the gate.
    private static bool RequiresReopen(AudioSettings current, AudioSettings next)
        => current.OutputDeviceId != next.OutputDeviceId
        || current.BufferMilliseconds != next.BufferMilliseconds
        || current.CueOutputDeviceId != next.CueOutputDeviceId
        || current.MasterOutputPair != next.MasterOutputPair
        || current.CueOutputPair != next.CueOutputPair;

    // Wrap the native seam so an unexpected fault never escapes the coordinator as an unhandled
    // exception that would leave the audio state ambiguous; an expected device error is the seam's
    // own `false` return.
    private bool TryReinitialize(AudioSettings settings)
    {
        try
        {
            return _reinitializer.Reinitialize(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio re-init threw for device '{Device}'.",
                settings.OutputDeviceId ?? "(system default)");
            return false;
        }
    }
}
