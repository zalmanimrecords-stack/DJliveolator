using Liveolator.Core.Settings;
using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Controls the live MIDI connection while also exposing its current status.
/// </summary>
public interface IMidiControlSession : IMidiControlStatus
{
    ControllerMappingProfile? ActiveProfile { get; }

    bool IsLearnArmed { get; }

    event EventHandler<ControllerMappingProfile>? MappingChanged;

    Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default);

    void Stop();

    void BeginLearn(
        PerformanceActionKind action,
        int slot = 0,
        string? argument = null,
        ActionInputMode? preferredInputMode = null);

    void CancelLearn();

    Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default);
}
