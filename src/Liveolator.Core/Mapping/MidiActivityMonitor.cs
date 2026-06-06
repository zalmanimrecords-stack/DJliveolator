namespace Liveolator.Core.Mapping;

/// <summary>
/// Surfaces a "MIDI is arriving from the device" pulse for the UI (the shell's connection LED), kept
/// deliberately separate from <see cref="MidiControllerRouter"/>: it observes the very same
/// <see cref="IMidiInput.MessageReceived"/> stream (the event is multicast) but never maps or
/// dispatches, so the activity cue works even when no mapping profile is loaded. One responsibility —
/// raise <see cref="ActivityDetected"/> per inbound message — and detach on dispose so the input can
/// be replaced on device change.
/// </summary>
public sealed class MidiActivityMonitor : IDisposable
{
    private readonly IMidiInput _input;
    private bool _disposed;

    public MidiActivityMonitor(IMidiInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _input.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Raised for each inbound MIDI message while attached. Fires on the MIDI callback thread — a UI
    /// subscriber must marshal to its own thread (the shell view-model does this via Rx).
    /// </summary>
    public event EventHandler? ActivityDetected;

    private void OnMessageReceived(object? sender, MidiMessage message)
        => ActivityDetected?.Invoke(this, EventArgs.Empty);

    /// <summary>Detaches from the input so the monitor can be replaced on device change.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _input.MessageReceived -= OnMessageReceived;
        _disposed = true;
    }
}
