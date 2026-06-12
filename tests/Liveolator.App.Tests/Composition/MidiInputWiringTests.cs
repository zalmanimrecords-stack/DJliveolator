using System;
using System.Collections.Generic;
using Liveolator.App.Composition;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// Covers the composition root's MIDI-input wiring (ServiceConfig.TryOpenMidiPipeline): it must open
/// the SETTINGS-chosen controller into the dispatcher when present, and DEGRADE GRACEFULLY — log and
/// run without MIDI, never throw — when no controller is selected, none matches, or the native open
/// fails (global standards #16/#26). Uses a fake device provider so no native rtmidi/hardware is needed.
/// </summary>
public sealed class MidiInputWiringTests
{
    [Fact]
    public void NoControllerSelected_YieldsNoPipeline_AppRunsWithoutMidi()
    {
        var provider = new FakeMidiDeviceProvider();
        var dispatcher = new RecordingDispatcher();

        MidiInputPipeline? pipeline = ServiceConfig.TryOpenMidiPipeline(
            provider, dispatcher, MidiSettings.Default);

        Assert.Null(pipeline);
        Assert.False(provider.OpenInputCalled);
    }

    [Fact]
    public void NoControllerConfigured_DetectedCmdStudio_IsSelectedAutomatically()
    {
        var provider = new FakeMidiDeviceProvider();
        provider.InputNames.Add("CMD Studio 2a");

        MidiSettings resolved = ServiceConfig.ResolveMidiSettings(MidiSettings.Default, provider);

        Assert.Equal("CMD Studio 2a", resolved.ControllerInputName);
    }

    [Fact]
    public void ConfiguredController_IsNotReplacedByAutoDetection()
    {
        var provider = new FakeMidiDeviceProvider();
        provider.InputNames.Add("CMD Studio 2a");
        var configured = new MidiSettings { ControllerInputName = "Ableton Push" };

        MidiSettings resolved = ServiceConfig.ResolveMidiSettings(configured, provider);

        Assert.Equal("Ableton Push", resolved.ControllerInputName);
    }

    [Fact]
    public void SelectedControllerNotFound_YieldsNoPipeline_DoesNotThrow()
    {
        var provider = new FakeMidiDeviceProvider { InputToReturn = null };
        var dispatcher = new RecordingDispatcher();
        var settings = new MidiSettings { ControllerInputName = "CMD Studio 2A" };

        MidiInputPipeline? pipeline = ServiceConfig.TryOpenMidiPipeline(provider, dispatcher, settings);

        Assert.Null(pipeline);
    }

    [Fact]
    public void OpenInputThrows_IsCaught_YieldsNoPipeline()
    {
        var provider = new FakeMidiDeviceProvider { ThrowOnOpenInput = true };
        var dispatcher = new RecordingDispatcher();
        var settings = new MidiSettings { ControllerInputName = "CMD Studio 2A" };

        Exception? ex = Record.Exception(
            () => ServiceConfig.TryOpenMidiPipeline(provider, dispatcher, settings));

        Assert.Null(ex); // the native failure must not escape the composition root
    }

    [Fact]
    public void ControllerFound_OpensPipeline_AutoSelectsCmdStudioProfile_AndRoutesToDispatcher()
    {
        var input = new FakeMidiInput("CMD Studio 2A");
        var provider = new FakeMidiDeviceProvider { InputToReturn = input };
        var dispatcher = new RecordingDispatcher();
        var settings = new MidiSettings { ControllerInputName = "CMD Studio 2A" };

        using MidiInputPipeline? pipeline = ServiceConfig.TryOpenMidiPipeline(provider, dispatcher, settings);

        Assert.NotNull(pipeline);
        Assert.Same(CmdStudio2AProfile.Default, pipeline!.ActiveProfile);
        Assert.True(input.IsOpen);

        // The default profile maps a play/pause note on Deck A (channel 0) — emitting it drives the
        // dispatcher, proving the hardware path reaches the one dispatcher.
        var playPause = FindBinding(PerformanceActionKind.DeckPlayPause, slot: 0);
        input.Emit(new MidiMessage(playPause.TriggerType, playPause.Channel, playPause.Data1, 127));

        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckPlayPause && a.Slot == 0);
    }

    [Fact]
    public void FeedbackOutputSelected_IsOpened_AndUsed()
    {
        var input = new FakeMidiInput("CMD Studio 2A");
        var output = new FakeMidiOutput("CMD Studio 2A");
        var provider = new FakeMidiDeviceProvider { InputToReturn = input, OutputToReturn = output };
        var dispatcher = new RecordingDispatcher();
        var settings = new MidiSettings
        {
            ControllerInputName = "CMD Studio 2A",
            FeedbackOutputName = "CMD Studio 2A",
        };

        using MidiInputPipeline? pipeline = ServiceConfig.TryOpenMidiPipeline(provider, dispatcher, settings);

        Assert.NotNull(pipeline);
        Assert.True(provider.OpenOutputCalled);

        // Sync feedback is routed to the bound control LED.
        var sync = FindBinding(PerformanceActionKind.DeckSyncOnce, slot: 0);
        dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.DeckSyncOnce, Slot: 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0)));

        Assert.Contains(output.Sent, m => m.Data1 == sync.Data1 && m.Channel == sync.Channel);
    }

    private static ControllerBinding FindBinding(PerformanceActionKind kind, int slot)
    {
        foreach (ControllerBinding b in CmdStudio2AProfile.Default.Bindings)
            if (b.Action == kind && b.Slot == slot)
                return b;
        throw new InvalidOperationException($"No default binding for {kind} slot {slot}.");
    }

    // --- Fakes (no native rtmidi) ---------------------------------------------------------------

    private sealed class FakeMidiDeviceProvider : IMidiDeviceProvider
    {
        public IMidiInput? InputToReturn { get; set; }
        public IMidiOutput? OutputToReturn { get; set; }
        public bool ThrowOnOpenInput { get; set; }
        public bool OpenInputCalled { get; private set; }
        public bool OpenOutputCalled { get; private set; }
        public List<string> InputNames { get; } = new();

        public IReadOnlyList<string> GetInputDeviceNames() => InputNames;
        public IReadOnlyList<string> GetOutputDeviceNames() => Array.Empty<string>();

        public IMidiInput? OpenInput(string deviceName)
        {
            OpenInputCalled = true;
            if (ThrowOnOpenInput)
                throw new InvalidOperationException("native open boom");
            return InputToReturn;
        }

        public IMidiOutput? OpenOutput(string deviceName)
        {
            OpenOutputCalled = true;
            return OutputToReturn;
        }
    }

    private sealed class FakeMidiInput : IMidiInput
    {
        public FakeMidiInput(string deviceName) => DeviceName = deviceName;
        public string DeviceName { get; }
        public bool IsOpen { get; private set; }
        public event EventHandler<MidiMessage>? MessageReceived;
        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;
        public void Emit(MidiMessage message) => MessageReceived?.Invoke(this, message);
        public void Dispose() => IsOpen = false;
    }

    private sealed class FakeMidiOutput : IMidiOutput
    {
        public FakeMidiOutput(string deviceName) => DeviceName = deviceName;
        public string DeviceName { get; }
        public List<MidiMessage> Sent { get; } = new();
        public void Send(MidiMessage message) => Sent.Add(message);
        public void SendSysEx(ReadOnlyMemory<byte> data) { }
        public void Dispose() { }
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Dispatched { get; } = new();
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged;
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => ActionFeedbackState.Unavailable;
        public void RaiseFeedback(ActionFeedbackChanged change) => FeedbackChanged?.Invoke(this, change);
    }
}
