using Liveolator.Core.Settings;
using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Controls the live MIDI connection while also exposing its current status.
/// </summary>
public interface IMidiControlSession : IMidiControlStatus
{
    ControllerMappingProfile? ActiveProfile { get; }

    /// <summary>
    /// The profiles this session can auto-select from — the shipped <c>mappings/*.json</c> plus the
    /// built-in profile classes. Offered for manual choice because auto-selection matches on the
    /// device's reported name, and a hub, a firmware revision or a driver's naming can leave a
    /// perfectly supported controller reporting a name no hint matches.
    /// </summary>
    /// <remarks>Defaulted so a session with no catalog (test doubles) simply offers no choice.</remarks>
    IReadOnlyList<ControllerMappingProfile> AvailableProfiles => Array.Empty<ControllerMappingProfile>();

    /// <summary>
    /// Applies <paramref name="profile"/> to the connected controller and saves it under that device's
    /// name, so it is also what loads on the next start. Returns false when no controller is connected,
    /// since there is then no device to key the profile to.
    /// </summary>
    Task<bool> ApplyProfileAsync(
        ControllerMappingProfile profile, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    bool IsLearnArmed { get; }

    event EventHandler<ControllerMappingProfile>? MappingChanged;

    Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default);

    void Stop();

    void BeginLearn(
        PerformanceActionKind action,
        int slot = 0,
        string? argument = null,
        ActionInputMode? preferredInputMode = null,
        double relativeTicksPerRevolution = 1.0,
        bool invert = false,
        RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement);

    void CancelLearn();

    Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default);
}
