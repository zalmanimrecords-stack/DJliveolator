using Liveolator.Core.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Midi;

/// <summary>
/// RtMidi.Core-backed <see cref="IMidiDeviceProvider"/> (doc 05): enumerates attached MIDI devices
/// for the Mappings UI and opens a named device as a Core <see cref="IMidiInput"/>/<see
/// cref="IMidiOutput"/>. This is the public entry point of the MIDI binding — the App composes one
/// provider, lists devices, and asks it to open the controller it wants to route. The native device
/// list lives behind <see cref="IRtMidiDeviceManager"/>, so name-lookup logic is unit-tested with a
/// fake and the native rtmidi library is not required in CI.
/// </summary>
public sealed class RtMidiDeviceProvider : IMidiDeviceProvider
{
    private readonly IRtMidiDeviceManager _manager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RtMidiDeviceProvider> _logger;

    public RtMidiDeviceProvider(ILoggerFactory? loggerFactory = null)
        : this(new RtMidiDeviceManager(), loggerFactory)
    {
    }

    internal RtMidiDeviceProvider(IRtMidiDeviceManager manager, ILoggerFactory? loggerFactory)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<RtMidiDeviceProvider>();
    }

    public IReadOnlyList<string> GetInputDeviceNames()
        => Enumerate(() => _manager.InputDevices.Select(d => d.Name), "input");

    public IReadOnlyList<string> GetOutputDeviceNames()
        => Enumerate(() => _manager.OutputDevices.Select(d => d.Name), "output");

    /// <summary>
    /// Opens the first input device whose name contains <paramref name="deviceName"/>
    /// (case-insensitive, same matching spirit as <see cref="MidiProfileSelector"/>). Returns null
    /// when no device matches; the caller surfaces "controller not found" to the UI (doc 12). The
    /// returned input is not yet open — call <see cref="IMidiInput.Open"/>.
    /// </summary>
    public IMidiInput? OpenInput(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        try
        {
            var info = _manager.InputDevices.FirstOrDefault(d => Matches(d.Name, deviceName));
            if (info is null)
            {
                _logger.LogWarning("No MIDI input device matched '{DeviceName}'", deviceName);
                return null;
            }

            return new RtMidiInput(info.CreateDevice(), _loggerFactory.CreateLogger<RtMidiInput>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MIDI input device matching '{DeviceName}'", deviceName);
            throw;
        }
    }

    /// <summary>
    /// Opens the first output (feedback) device matching <paramref name="deviceName"/>, or null when
    /// none match. Output is optional — control still works without it (doc 06).
    /// </summary>
    public IMidiOutput? OpenOutput(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        try
        {
            var info = _manager.OutputDevices.FirstOrDefault(d => Matches(d.Name, deviceName));
            if (info is null)
            {
                _logger.LogWarning("No MIDI output device matched '{DeviceName}'", deviceName);
                return null;
            }

            return new RtMidiOutput(info.CreateDevice(), _loggerFactory.CreateLogger<RtMidiOutput>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MIDI output device matching '{DeviceName}'", deviceName);
            throw;
        }
    }

    private static bool Matches(string actual, string requested)
        => actual.Contains(requested, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> Enumerate(Func<IEnumerable<string>> names, string direction)
    {
        try
        {
            return names().ToList();
        }
        catch (Exception ex)
        {
            // Enumeration can throw if the MIDI subsystem is unavailable; surface an empty list so
            // the UI shows "no devices" rather than crashing (doc 05 error handling).
            _logger.LogError(ex, "Enumerating MIDI {Direction} devices failed", direction);
            return Array.Empty<string>();
        }
    }
}
