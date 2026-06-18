using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>Records dispatched actions so mapper tests can assert what was produced.</summary>
internal sealed class RecordingDispatcher : IPerformanceActionDispatcher
{
    public List<PerformanceAction> Dispatched { get; } = new();

    public bool ThrowOnDispatch { get; set; }

    /// <summary>Current value reported by <see cref="GetFeedback"/> (the soft-takeover target).</summary>
    public double FeedbackValue { get; set; }

    public event EventHandler<ActionFeedbackChanged>? FeedbackChanged;

    public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }

    public void Dispatch(PerformanceAction action)
    {
        if (ThrowOnDispatch)
            throw new InvalidOperationException("dispatch boom");
        Dispatched.Add(action);
    }

    public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
        => new(IsActive: false, IsAvailable: true, Value: FeedbackValue);

    public void RaiseFeedback(ActionFeedbackChanged change) => FeedbackChanged?.Invoke(this, change);
}

/// <summary>A MIDI input whose messages tests inject via <see cref="Emit"/>.</summary>
internal sealed class FakeMidiInput : IMidiInput
{
    public FakeMidiInput(string deviceName = "Fake Controller") => DeviceName = deviceName;

    public string DeviceName { get; }

    public bool IsOpen { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler<MidiMessage>? MessageReceived;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void Emit(MidiMessage message) => MessageReceived?.Invoke(this, message);

    public bool HasSubscribers => MessageReceived is not null;

    public void Dispose() => Disposed = true;
}

/// <summary>A MIDI output that records what it was told to send.</summary>
internal sealed class FakeMidiOutput : IMidiOutput
{
    public FakeMidiOutput(string deviceName = "Fake Controller") => DeviceName = deviceName;

    public string DeviceName { get; }

    public List<MidiMessage> Sent { get; } = new();

    public List<byte[]> SysEx { get; } = new();

    public void Send(MidiMessage message) => Sent.Add(message);

    public void SendSysEx(ReadOnlyMemory<byte> data) => SysEx.Add(data.ToArray());

    public void Dispose() { }
}
