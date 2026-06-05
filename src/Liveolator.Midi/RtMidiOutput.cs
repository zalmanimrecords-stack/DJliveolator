using Liveolator.Core.Mapping;
using Microsoft.Extensions.Logging;
using RtMidi.Core.Enums;
using RtMidi.Core.Messages;
using RtMidiDevice = RtMidi.Core.Devices.IMidiOutputDevice;

namespace Liveolator.Midi;

/// <summary>
/// Adapts an RtMidi.Core output device to the Core <see cref="IMidiOutput"/> seam (doc 05/06): turns
/// neutral feedback <see cref="MidiMessage"/>s into RtMidi note/CC sends and forwards raw SysEx for
/// Push LCD/mode control. Feedback must never block the input path, so send failures are logged and
/// swallowed (doc 06). The device is opened lazily on first send.
/// </summary>
internal sealed class RtMidiOutput : IMidiOutput
{
    private readonly RtMidiDevice _device;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private bool _disposed;

    internal RtMidiOutput(RtMidiDevice device, ILogger logger)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string DeviceName => _device.Name;

    public void Send(MidiMessage message)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!EnsureOpen()) return;

            try
            {
                bool sent = message.Type switch
                {
                    MidiMessageType.NoteOn => _device.Send(new NoteOnMessage((Channel)message.Channel, (Key)message.Data1, message.Data2)),
                    MidiMessageType.NoteOff => _device.Send(new NoteOffMessage((Channel)message.Channel, (Key)message.Data1, message.Data2)),
                    MidiMessageType.ControlChange => _device.Send(new ControlChangeMessage((Channel)message.Channel, message.Data1, message.Data2)),
                    // PitchBend has no LED/feedback use; ignore quietly at debug (doc 05/06).
                    _ => LogUnsupported(message),
                };

                if (!sent)
                    _logger.LogWarning("MIDI output {Device} rejected feedback message {Message}", _device.Name, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed sending MIDI feedback to {Device}: {Message}", _device.Name, message);
            }
        }
    }

    public void SendSysEx(ReadOnlyMemory<byte> data)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!EnsureOpen()) return;

            try
            {
                if (!_device.Send(new SysExMessage(data.ToArray())))
                    _logger.LogWarning("MIDI output {Device} rejected a {Length}-byte SysEx payload", _device.Name, data.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed sending SysEx to {Device} ({Length} bytes)", _device.Name, data.Length);
            }
        }
    }

    private bool LogUnsupported(MidiMessage message)
    {
        _logger.LogDebug("Ignoring unsupported feedback message type {Type} for {Device}", message.Type, _device.Name);
        return true; // not an error; nothing was sent but nothing failed.
    }

    private bool EnsureOpen()
    {
        if (_device.IsOpen) return true;
        try
        {
            if (_device.Open()) return true;
            _logger.LogWarning("RtMidi reported failure opening output device {Device}; feedback disabled", _device.Name);
            return false;
        }
        catch (Exception ex)
        {
            // Output unavailable must not block input (doc 06): degrade silently-but-logged.
            _logger.LogWarning(ex, "Could not open MIDI output device {Device}; feedback disabled", _device.Name);
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _device.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MIDI output device {Device}", _device.Name);
            }
        }
    }
}
