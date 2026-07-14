using Liveolator.Midi;
using RtMidi.Core.Devices;
using RtMidi.Core.Devices.Infos;

namespace Liveolator.Midi.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IRtMidiDeviceManager"/>: returns canned device-info lists so the
/// provider's enumeration and name-lookup logic runs with no native rtmidi library.
/// </summary>
internal sealed class FakeRtMidiDeviceManager : IRtMidiDeviceManager
{
    private readonly List<IMidiInputDeviceInfo> _inputs = new();
    private readonly List<IMidiOutputDeviceInfo> _outputs = new();

    public bool ThrowOnEnumerate { get; set; }

    public IEnumerable<IMidiInputDeviceInfo> InputDevices
        => ThrowOnEnumerate ? throw new InvalidOperationException("midi subsystem unavailable") : _inputs;

    public IEnumerable<IMidiOutputDeviceInfo> OutputDevices
        => ThrowOnEnumerate ? throw new InvalidOperationException("midi subsystem unavailable") : _outputs;

    public FakeInputDeviceInfo AddInput(string name)
    {
        var info = new FakeInputDeviceInfo(name);
        _inputs.Add(info);
        return info;
    }

    public FakeOutputDeviceInfo AddOutput(string name)
    {
        var info = new FakeOutputDeviceInfo(name);
        _outputs.Add(info);
        return info;
    }
}

internal sealed class FakeInputDeviceInfo : IMidiInputDeviceInfo
{
    public FakeInputDeviceInfo(string name) => Name = name;

    public string Name { get; }

    public FakeInputDevice? Created { get; private set; }

    public IMidiInputDevice CreateDevice() => Created = new FakeInputDevice(Name);
}

internal sealed class FakeOutputDeviceInfo : IMidiOutputDeviceInfo
{
    public FakeOutputDeviceInfo(string name) => Name = name;

    public string Name { get; }

    public FakeOutputDevice? Created { get; private set; }

    public IMidiOutputDevice CreateDevice() => Created = new FakeOutputDevice(Name);
}
