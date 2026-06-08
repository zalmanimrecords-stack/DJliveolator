using Liveolator.Core.Settings;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Controls the live MIDI connection while also exposing its current status.
/// </summary>
public interface IMidiControlSession : IMidiControlStatus
{
    Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default);

    void Stop();
}
