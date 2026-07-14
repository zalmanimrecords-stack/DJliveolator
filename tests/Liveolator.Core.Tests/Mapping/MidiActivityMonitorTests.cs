using Liveolator.Core.Mapping;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// Verifies the lightweight activity monitor: it surfaces an "a message arrived" pulse off the same
/// input the router listens to (so the UI can flash a connection LED), and detaches on dispose.
/// </summary>
public sealed class MidiActivityMonitorTests
{
    private readonly FakeMidiInput _input = new();

    [Fact]
    public void IncomingMessage_RaisesActivityDetected()
    {
        using var monitor = new MidiActivityMonitor(_input);
        int activity = 0;
        monitor.ActivityDetected += (_, _) => activity++;

        _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        _input.Emit(new MidiMessage(MidiMessageType.ControlChange, 0, 7, 64));

        Assert.Equal(2, activity);
    }

    [Fact]
    public void Dispose_StopsRaisingActivity()
    {
        var monitor = new MidiActivityMonitor(_input);
        int activity = 0;
        monitor.ActivityDetected += (_, _) => activity++;

        monitor.Dispose();
        _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.Equal(0, activity);
        Assert.False(_input.HasSubscribers);
    }

    [Fact]
    public void NullInput_Throws()
        => Assert.Throws<ArgumentNullException>(() => new MidiActivityMonitor(null!));
}
