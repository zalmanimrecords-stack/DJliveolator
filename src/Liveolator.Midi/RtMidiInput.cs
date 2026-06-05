using Liveolator.Core.Mapping;
using Microsoft.Extensions.Logging;
using RtMidiDevice = RtMidi.Core.Devices.IMidiInputDevice;
using RtMidiMessages = RtMidi.Core.Messages;

namespace Liveolator.Midi;

/// <summary>
/// Adapts an RtMidi.Core input device to the Core <see cref="IMidiInput"/> seam (doc 05): subscribes
/// to the device's per-message events, translates each to a neutral <see cref="MidiMessage"/> via
/// <see cref="RtMidiMessageTranslator"/>, and re-raises them. The native rtmidi library is touched
/// only inside <see cref="Open"/>; constructing this adapter and wiring events is pure managed code,
/// so the translation path is covered by tests without any device (mirrors DeckAudioSource/BASS).
/// </summary>
internal sealed class RtMidiInput : IMidiInput
{
    private readonly RtMidiDevice _device;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private bool _subscribed;
    private bool _disposed;

    internal RtMidiInput(RtMidiDevice device, ILogger logger)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string DeviceName => _device.Name;

    public bool IsOpen
    {
        get { lock (_gate) return _device.IsOpen; }
    }

    public event EventHandler<MidiMessage>? MessageReceived;

    public void Open()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RtMidiInput));
            if (_device.IsOpen) return;

            try
            {
                if (!_subscribed)
                {
                    _device.NoteOn += OnNoteOn;
                    _device.NoteOff += OnNoteOff;
                    _device.ControlChange += OnControlChange;
                    _device.PitchBend += OnPitchBend;
                    _subscribed = true;
                }

                if (!_device.Open())
                    throw new InvalidOperationException($"RtMidi reported failure opening input device '{_device.Name}'.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open MIDI input device {Device}", _device.Name);
                throw;
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_disposed || !_device.IsOpen) return;

            try
            {
                _device.Close();
            }
            catch (Exception ex)
            {
                // A disconnect mid-set can throw on close; log and continue (doc 05) — the profile
                // stays loaded for reconnect and the app must not crash on teardown.
                _logger.LogWarning(ex, "Error closing MIDI input device {Device}", _device.Name);
            }
        }
    }

    // RtMidi raises these on its own callback thread. Translation is pure; re-raising honours the
    // subscriber's handler. We never throw back into the native callback (would crash rtmidi).
    private void OnNoteOn(RtMidiDevice sender, in RtMidiMessages.NoteOnMessage msg)
        => Raise(RtMidiMessageTranslator.FromNoteOn(msg));

    private void OnNoteOff(RtMidiDevice sender, in RtMidiMessages.NoteOffMessage msg)
        => Raise(RtMidiMessageTranslator.FromNoteOff(msg));

    private void OnControlChange(RtMidiDevice sender, in RtMidiMessages.ControlChangeMessage msg)
        => Raise(RtMidiMessageTranslator.FromControlChange(msg));

    private void OnPitchBend(RtMidiDevice sender, in RtMidiMessages.PitchBendMessage msg)
        => Raise(RtMidiMessageTranslator.FromPitchBend(msg));

    private void Raise(MidiMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MIDI input handler threw for {Device} message {Message}", _device.Name, message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_subscribed)
            {
                _device.NoteOn -= OnNoteOn;
                _device.NoteOff -= OnNoteOff;
                _device.ControlChange -= OnControlChange;
                _device.PitchBend -= OnPitchBend;
                _subscribed = false;
            }

            try
            {
                _device.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MIDI input device {Device}", _device.Name);
            }
        }
    }
}
