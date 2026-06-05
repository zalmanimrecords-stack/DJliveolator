using RtMidi.Core.Devices.Infos;

namespace Liveolator.Midi;

/// <summary>
/// Thin internal seam over <c>RtMidi.Core.MidiDeviceManager.Default</c> — the single place that
/// touches the native rtmidi device list. Isolating it (mirroring the Audio binding's
/// <c>IBassPlayback</c>) lets the provider's enumeration/lookup logic unit-test with a fake, so the
/// native library is never required in CI.
/// </summary>
internal interface IRtMidiDeviceManager
{
    IEnumerable<IMidiInputDeviceInfo> InputDevices { get; }

    IEnumerable<IMidiOutputDeviceInfo> OutputDevices { get; }
}
