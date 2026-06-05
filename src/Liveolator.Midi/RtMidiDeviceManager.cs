using RtMidi.Core;
using RtMidi.Core.Devices.Infos;

namespace Liveolator.Midi;

/// <summary>
/// Production <see cref="IRtMidiDeviceManager"/> backed by the real
/// <c>RtMidi.Core.MidiDeviceManager.Default</c>. Enumerating the lists is where the native rtmidi
/// library is queried, so this type is never exercised by unit tests (they use a fake) — keeping it
/// a one-line forward keeps that untested surface minimal.
/// </summary>
internal sealed class RtMidiDeviceManager : IRtMidiDeviceManager
{
    public IEnumerable<IMidiInputDeviceInfo> InputDevices => MidiDeviceManager.Default.InputDevices;

    public IEnumerable<IMidiOutputDeviceInfo> OutputDevices => MidiDeviceManager.Default.OutputDevices;
}
