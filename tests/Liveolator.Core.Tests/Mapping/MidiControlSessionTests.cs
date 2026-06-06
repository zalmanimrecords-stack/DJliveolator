using Liveolator.Core.Actions;
using Liveolator.Core.Autopilot;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// Verifies the runtime MIDI pipeline orchestration: opening the configured controller, loading (or
/// falling back to an empty) profile, routing mapped messages to the dispatcher, surfacing the
/// activity pulse, and tearing the devices down on stop. All with fakes — no native MIDI.
/// </summary>
public sealed class MidiControlSessionTests
{
    private static readonly ControllerBinding PadBinding = new(
        MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary);

    private readonly RecordingDispatcher _dispatcher = new();
    private readonly FakeLiveProfileStore _store = new();
    private readonly FakeMidiDeviceProvider _provider = new();

    private MidiControlSession NewSession()
        => new(_provider, _dispatcher, _store, new MidiLearnSession(), NullLoggerFactory.Instance);

    [Fact]
    public async Task StartAsync_OpensAndConnectsTheSelectedController()
    {
        using var session = NewSession();

        await session.StartAsync(new MidiSettings { ControllerInputName = "Push" });

        Assert.True(session.IsInputConnected);
        Assert.Equal("Ableton Push", session.InputDeviceName);
        Assert.True(_provider.LastInput!.IsOpen);
    }

    [Fact]
    public async Task StartAsync_WithNoController_StaysIdle()
    {
        using var session = NewSession();

        await session.StartAsync(MidiSettings.Default);

        Assert.False(session.IsInputConnected);
        Assert.Null(_provider.LastInput);
    }

    [Fact]
    public async Task StartAsync_WhenDeviceNotFound_DoesNotConnect_AndDoesNotThrow()
    {
        _provider.InputToReturn = null;
        using var session = NewSession();

        var exception = await Record.ExceptionAsync(
            () => session.StartAsync(new MidiSettings { ControllerInputName = "Ghost" }));

        Assert.Null(exception);
        Assert.False(session.IsInputConnected);
    }

    [Fact]
    public async Task LoadedProfileBinding_RoutesIncomingMessageToDispatcher()
    {
        _store.Profile = new ControllerMappingProfile("Push", "Push", new[] { PadBinding });
        using var session = NewSession();
        await session.StartAsync(new MidiSettings { ControllerInputName = "Push" });

        _provider.LastInput!.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        PerformanceAction action = Assert.Single(_dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualBlackout, action.Kind);
    }

    [Fact]
    public async Task NoProfileOnDisk_FallsBackToEmpty_StillFlashesActivity_ButDispatchesNothing()
    {
        _store.Profile = null; // nothing saved for this device
        using var session = NewSession();
        int activity = 0;
        session.ActivityDetected += (_, _) => activity++;
        await session.StartAsync(new MidiSettings { ControllerInputName = "Push" });

        _provider.LastInput!.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.Equal(1, activity);
        Assert.Empty(_dispatcher.Dispatched);
        Assert.Empty(session.ActiveProfile!.Bindings);
    }

    [Fact]
    public async Task FeedbackOutput_WhenConfigured_IsOpenedAndConnected()
    {
        using var session = NewSession();

        await session.StartAsync(new MidiSettings
        {
            ControllerInputName = "Push",
            FeedbackOutputName = "Push",
        });

        Assert.True(session.IsOutputConnected);
        Assert.Equal("Ableton Push", session.OutputDeviceName);
    }

    [Fact]
    public async Task Stop_ClosesAndDisposesInput_AndStopsActivity()
    {
        using var session = NewSession();
        int activity = 0;
        session.ActivityDetected += (_, _) => activity++;
        await session.StartAsync(new MidiSettings { ControllerInputName = "Push" });
        FakeMidiInput input = _provider.LastInput!;

        session.Stop();
        input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.False(input.IsOpen);
        Assert.True(input.Disposed);
        Assert.False(session.IsInputConnected);
        Assert.Equal(0, activity);
    }

    /// <summary>A device provider that hands out a controllable fake input/output and records requests.</summary>
    private sealed class FakeMidiDeviceProvider : IMidiDeviceProvider
    {
        public FakeMidiInput? InputToReturn { get; set; } = new("Ableton Push");
        public FakeMidiOutput? OutputToReturn { get; set; } = new("Ableton Push");

        public FakeMidiInput? LastInput { get; private set; }

        public IReadOnlyList<string> GetInputDeviceNames() => new[] { "Ableton Push" };
        public IReadOnlyList<string> GetOutputDeviceNames() => new[] { "Ableton Push" };

        public IMidiInput? OpenInput(string deviceName)
        {
            LastInput = InputToReturn;
            return InputToReturn;
        }

        public IMidiOutput? OpenOutput(string deviceName) => OutputToReturn;
    }

    /// <summary>Returns a single configured mapping profile (or null); other Live data is unused here.</summary>
    private sealed class FakeLiveProfileStore : ILiveProfileStore
    {
        public ControllerMappingProfile? Profile { get; set; }

        public Task<ControllerMappingProfile?> LoadMappingProfileAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Profile);

        public Task SaveMappingProfileAsync(ControllerMappingProfile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<VisualBank?> LoadVisualBankAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<VisualBank?>(null);

        public Task SaveVisualBankAsync(VisualBank bank, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<VisualMacro>> LoadVisualMacrosAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VisualMacro>>(Array.Empty<VisualMacro>());

        public Task SaveVisualMacrosAsync(IEnumerable<VisualMacro> macros, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AutopilotRuleSet?> LoadAutopilotRuleSetAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<AutopilotRuleSet?>(null);

        public Task SaveAutopilotRuleSetAsync(AutopilotRuleSet ruleSet, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
