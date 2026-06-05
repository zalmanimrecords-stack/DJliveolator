using Liveolator.Core.Mapping;
using Liveolator.Midi;
using Liveolator.Midi.Tests.Fakes;

namespace Liveolator.Midi.Tests;

/// <summary>
/// Device enumeration and name-lookup over a fake <see cref="IRtMidiDeviceManager"/> — no native
/// rtmidi library is touched.
/// </summary>
public sealed class RtMidiDeviceProviderTests
{
    private static RtMidiDeviceProvider Provider(FakeRtMidiDeviceManager manager)
        => new(manager, loggerFactory: null);

    [Fact]
    public void GetInputDeviceNames_lists_attached_inputs()
    {
        var manager = new FakeRtMidiDeviceManager();
        manager.AddInput("Ableton Push");
        manager.AddInput("CMD STUDIO 2A");

        IReadOnlyList<string> names = Provider(manager).GetInputDeviceNames();

        Assert.Equal(new[] { "Ableton Push", "CMD STUDIO 2A" }, names);
    }

    [Fact]
    public void GetOutputDeviceNames_lists_attached_outputs()
    {
        var manager = new FakeRtMidiDeviceManager();
        manager.AddOutput("Ableton Push");

        Assert.Equal(new[] { "Ableton Push" }, Provider(manager).GetOutputDeviceNames());
    }

    [Fact]
    public void Enumeration_failure_returns_empty_list_not_throws()
    {
        var manager = new FakeRtMidiDeviceManager { ThrowOnEnumerate = true };

        Assert.Empty(Provider(manager).GetInputDeviceNames());
        Assert.Empty(Provider(manager).GetOutputDeviceNames());
    }

    [Fact]
    public void OpenInput_matches_by_case_insensitive_substring()
    {
        var manager = new FakeRtMidiDeviceManager();
        FakeInputDeviceInfo info = manager.AddInput("Ableton Push 2");

        IMidiInput? input = Provider(manager).OpenInput("push");

        Assert.NotNull(input);
        Assert.Equal("Ableton Push 2", input!.DeviceName);
        Assert.NotNull(info.Created); // a real device was created from the matched info
    }

    [Fact]
    public void OpenInput_returns_null_when_no_device_matches()
    {
        var manager = new FakeRtMidiDeviceManager();
        manager.AddInput("CMD STUDIO 2A");

        Assert.Null(Provider(manager).OpenInput("Push"));
    }

    [Fact]
    public void OpenOutput_matches_by_substring()
    {
        var manager = new FakeRtMidiDeviceManager();
        manager.AddOutput("Ableton Push 2");

        IMidiOutput? output = Provider(manager).OpenOutput("Push");

        Assert.NotNull(output);
        Assert.Equal("Ableton Push 2", output!.DeviceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OpenInput_rejects_blank_name(string name)
    {
        var manager = new FakeRtMidiDeviceManager();
        Assert.Throws<ArgumentException>(() => Provider(manager).OpenInput(name));
    }
}
