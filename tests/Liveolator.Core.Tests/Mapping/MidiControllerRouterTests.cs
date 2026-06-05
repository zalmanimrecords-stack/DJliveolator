using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Tests.Actions;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class MidiControllerRouterTests
{
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly FakeMidiInput _input = new();
    private readonly MidiLearnSession _learn = new();

    private static readonly ControllerBinding PadBinding = new(
        MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary);

    private MidiControllerRouter BuildRouter(ControllerMapper mapper)
        => new(_input, mapper, _learn, new CapturingLogger<MidiControllerRouter>());

    private ControllerMapper BuildMapper()
        => new(new ControllerMappingProfile("p", "device", new[] { PadBinding }),
            _dispatcher, new CapturingLogger<ControllerMapper>());

    [Fact]
    public void Message_WhenNotLearning_IsMappedAndDispatched()
    {
        using var router = BuildRouter(BuildMapper());

        _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        PerformanceAction action = Assert.Single(_dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualBlackout, action.Kind);
    }

    [Fact]
    public void Message_WhileLearning_IsCaptured_NotMapped()
    {
        using var router = BuildRouter(BuildMapper());
        ControllerBinding? learned = null;
        _learn.Learned += (_, b) => learned = b;
        _learn.Begin(PerformanceActionKind.BeatLock, slot: 1);

        _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 40, 127));

        Assert.NotNull(learned);
        Assert.Equal(PerformanceActionKind.BeatLock, learned!.Action);
        Assert.Empty(_dispatcher.Dispatched); // action was not fired during learn
    }

    [Fact]
    public void Dispose_UnsubscribesFromInput()
    {
        var router = BuildRouter(BuildMapper());
        router.Dispose();

        _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public void RoutingException_IsCaught_AndDoesNotEscape()
    {
        var logger = new CapturingLogger<MidiControllerRouter>();
        using var router = new MidiControllerRouter(_input, new ThrowingMapper(), _learn, logger);

        var exception = Record.Exception(() => _input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127)));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, e => e.Exception is not null);
    }

    private sealed class ThrowingMapper : IControllerMapper
    {
        public ControllerMappingProfile ActiveProfile { get; } = ControllerMappingProfile.Empty("p", "d");
        public void SetProfile(ControllerMappingProfile profile) { }
        public void Apply(MidiMessage message) => throw new InvalidOperationException("map boom");
    }
}
