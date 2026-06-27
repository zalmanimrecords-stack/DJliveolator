using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Tests.Actions;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class VisualActionHandlerTests
{
    private readonly VisualBank _bank;
    private readonly FakeVisualPerformanceEngine _engine;
    private readonly VisualActionHandler _handler;

    public VisualActionHandlerTests()
    {
        _bank = new VisualBank("Set A", new[]
        {
            Scene("Intro"),
            Scene("Drop"),
        });
        _engine = new FakeVisualPerformanceEngine(_bank);
        _handler = new VisualActionHandler(_engine);
    }

    private static VisualScene Scene(string name) => new(
        name,
        Array.Empty<VisualLayer>(),
        new Dictionary<string, double>(),
        TransitionStyle.Crossfade,
        BeatBehavior.None);

    private void Handle(PerformanceActionKind kind, int slot = 0, double value = 0, string? argument = null)
        => _handler.Handle(new PerformanceAction(kind, Slot: slot, Value: value, Argument: argument));

    [Fact]
    public void HandledKinds_CoverTheWiredVisualActions()
    {
        // Every declared Visual* kind has an owning handler (12 original + VisualLoadPreset + the
        // VisualSetLaunchQuantize launch-quantize mode, doc 28/31).
        Assert.Equal(14, _handler.HandledKinds.Count);
        Assert.Contains(PerformanceActionKind.VisualLoadScene, _handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.VisualTransitionNextBar, _handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.VisualLoadPreset, _handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.VisualSetLaunchQuantize, _handler.HandledKinds);
    }

    [Fact]
    public void LoadScene_LoadsTheBankSceneAtSlot_Immediately()
    {
        Handle(PerformanceActionKind.VisualLoadScene, slot: 1);

        var loaded = Assert.Single(_engine.LoadedScenes);
        Assert.Equal("Drop", loaded.Scene.Name);
        Assert.Equal(Quantize.Immediate, loaded.When);
    }

    [Fact]
    public void LoadScene_OutOfRangeSlot_DoesNotCallEngine()
    {
        Handle(PerformanceActionKind.VisualLoadScene, slot: 99);

        Assert.Empty(_engine.LoadedScenes);
    }

    [Fact]
    public void LoadScene_RaisesActiveFeedbackForTheLoadedSlot()
    {
        var feedback = new List<ActionFeedbackChanged>();
        _handler.FeedbackChanged += (_, e) => feedback.Add(e);

        Handle(PerformanceActionKind.VisualLoadScene, slot: 0);

        Assert.Contains(feedback, f =>
            f.Kind == PerformanceActionKind.VisualLoadScene && f.Slot == 0 && f.State.IsActive);
        Assert.True(_handler.GetFeedback(PerformanceActionKind.VisualLoadScene, slot: 0).IsActive);
        Assert.False(_handler.GetFeedback(PerformanceActionKind.VisualLoadScene, slot: 1).IsActive);
    }

    [Fact]
    public void LoadScene_SwitchingScene_ReleasesThePreviousPad()
    {
        Handle(PerformanceActionKind.VisualLoadScene, slot: 0);

        var feedback = new List<ActionFeedbackChanged>();
        _handler.FeedbackChanged += (_, e) => feedback.Add(e);

        Handle(PerformanceActionKind.VisualLoadScene, slot: 1);

        Assert.Contains(feedback, f =>
            f.Kind == PerformanceActionKind.VisualLoadScene && f.Slot == 0 && !f.State.IsActive);
        Assert.False(_handler.GetFeedback(PerformanceActionKind.VisualLoadScene, slot: 0).IsActive);
        Assert.True(_handler.GetFeedback(PerformanceActionKind.VisualLoadScene, slot: 1).IsActive);
    }

    [Fact]
    public void SelectBank_DrivesEngine_AndReportsFeedback()
    {
        ActionFeedbackChanged? feedback = null;
        _handler.FeedbackChanged += (_, e) => feedback = e;

        Handle(PerformanceActionKind.VisualSelectBank, slot: 2);

        Assert.Equal(2, Assert.Single(_engine.SelectedBanks));
        Assert.NotNull(feedback);
        Assert.Equal(PerformanceActionKind.VisualSelectBank, feedback!.Kind);
    }

    [Fact]
    public void SetMacro_PassesNameAndValue()
    {
        Handle(PerformanceActionKind.VisualSetMacro, value: 0.7, argument: "echo.feedback");

        var macro = Assert.Single(_engine.Macros);
        Assert.Equal("echo.feedback", macro.Name);
        Assert.Equal(0.7, macro.Value, precision: 6);
    }

    [Fact]
    public void SetMacro_RaisesValueFeedbackForUi()
    {
        ActionFeedbackChanged? feedback = null;
        _handler.FeedbackChanged += (_, e) => feedback = e;

        Handle(PerformanceActionKind.VisualSetMacro, value: 0.7, argument: "echo.feedback");

        Assert.Equal(PerformanceActionKind.VisualSetMacro, feedback!.Kind);
        Assert.Equal(0.7, feedback.State.Value);
        Assert.Equal("echo.feedback", feedback.State.Argument);
    }

    [Fact]
    public void SetMacro_WithoutName_DoesNotCallEngine()
    {
        Handle(PerformanceActionKind.VisualSetMacro, value: 0.5, argument: null);

        Assert.Empty(_engine.Macros);
    }

    [Fact]
    public void ToggleLayer_DrivesEngineWithSlot()
    {
        Handle(PerformanceActionKind.VisualToggleLayer, slot: 3);

        Assert.Equal(3, Assert.Single(_engine.ToggledLayers));
    }

    [Fact]
    public void SetLayerOpacity_PassesSlotAndValue()
    {
        Handle(PerformanceActionKind.VisualSetLayerOpacity, slot: 1, value: 0.25);

        var op = Assert.Single(_engine.Opacities);
        Assert.Equal(1, op.Layer);
        Assert.Equal(0.25, op.Opacity, precision: 6);
    }

    [Fact]
    public void SetLayerSource_DecodesSourceAndPassesItImmediately()
    {
        var source = new VisualSourceRef(VisualSourceKind.Generator, "core/vu-meter");

        Handle(
            PerformanceActionKind.VisualSetLayerSource,
            slot: 3,
            argument: VisualSourceActionCodec.Encode(source));

        var selected = Assert.Single(_engine.LayerSources);
        Assert.Equal(3, selected.Layer);
        Assert.Equal(source, selected.Source);
        Assert.Equal(Quantize.Immediate, selected.When);
    }

    [Fact]
    public void SetLayerSource_WithInvalidPayload_DoesNotCallEngine()
    {
        Handle(PerformanceActionKind.VisualSetLayerSource, slot: 0, argument: "not-json");

        Assert.Empty(_engine.LayerSources);
    }

    [Fact]
    public void SetLayerSource_WithNoneSource_ClearsTheLayer()
    {
        Handle(
            PerformanceActionKind.VisualSetLayerSource,
            slot: 2,
            argument: VisualSourceActionCodec.Encode(VisualSourceRef.None));

        var selected = Assert.Single(_engine.LayerSources);
        Assert.Equal(2, selected.Layer);
        Assert.Equal(VisualSourceKind.None, selected.Source.Kind);
    }

    [Fact]
    public void LaunchClip_PassesSlotAndClipId_Immediately()
    {
        Handle(PerformanceActionKind.VisualLaunchClip, slot: 2, argument: "clip-42");

        var clip = Assert.Single(_engine.LaunchedClips);
        Assert.Equal(2, clip.Layer);
        Assert.Equal("clip-42", clip.ClipId);
        Assert.Equal(Quantize.Immediate, clip.When);
    }

    [Fact]
    public void LaunchClip_WithoutClipId_DoesNotCallEngine()
    {
        Handle(PerformanceActionKind.VisualLaunchClip, slot: 0, argument: null);

        Assert.Empty(_engine.LaunchedClips);
    }

    [Fact]
    public void Blackout_TogglesLatch_AndReportsState()
    {
        Handle(PerformanceActionKind.VisualBlackout);
        Assert.True(_engine.BlackoutCalls[^1]);
        Assert.True(_handler.GetFeedback(PerformanceActionKind.VisualBlackout, 0).IsActive);

        Handle(PerformanceActionKind.VisualBlackout);
        Assert.False(_engine.BlackoutCalls[^1]);
        Assert.False(_handler.GetFeedback(PerformanceActionKind.VisualBlackout, 0).IsActive);
    }

    [Fact]
    public void Strobe_TogglesLatch_AndReportsState()
    {
        Handle(PerformanceActionKind.VisualToggleStrobe);
        Assert.True(_engine.StrobeCalls[^1]);
        Assert.True(_handler.GetFeedback(PerformanceActionKind.VisualToggleStrobe, 0).IsActive);

        Handle(PerformanceActionKind.VisualToggleStrobe);
        Assert.False(_engine.StrobeCalls[^1]);
        Assert.False(_handler.GetFeedback(PerformanceActionKind.VisualToggleStrobe, 0).IsActive);
    }

    [Theory]
    [InlineData(PerformanceActionKind.VisualTransitionNow, Quantize.Immediate)]
    [InlineData(PerformanceActionKind.VisualTransitionNextBeat, Quantize.NextBeat)]
    [InlineData(PerformanceActionKind.VisualTransitionNextBar, Quantize.NextBar)]
    public void Transition_MapsKindToQuantum(PerformanceActionKind kind, Quantize expected)
    {
        Handle(kind);

        var transition = Assert.Single(_engine.Transitions);
        Assert.Equal(expected, transition.When);
        Assert.Equal(VisualActionHandler.DefaultTransition, transition.Style);
    }

    [Fact]
    public void EndToEnd_DispatcherRoutesVisualBlackoutToTheEngine()
    {
        using var dispatcher = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { _handler }, new CapturingLogger<PerformanceActionDispatcher>());
        ActionFeedbackChanged? feedback = null;
        dispatcher.FeedbackChanged += (_, e) => feedback = e;

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualBlackout));

        Assert.True(_engine.BlackoutCalls[^1]);
        Assert.NotNull(feedback);
        Assert.True(dispatcher.GetFeedback(PerformanceActionKind.VisualBlackout).IsActive);
    }

    [Fact]
    public void Constructor_RejectsNullEngine()
        => Assert.Throws<ArgumentNullException>(() => new VisualActionHandler(null!));

    [Fact]
    public void LoadScene_WhenQuantized_DefersToTheSchedulerInsteadOfLoadingNow()
    {
        var scheduler = new CapturingBeatScheduler();
        var handler = new VisualActionHandler(_engine, scheduler: scheduler) { LaunchQuantize = Quantize.NextBar };

        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualLoadScene, Slot: 1));

        Assert.Empty(_engine.LoadedScenes);            // not loaded yet — waiting for the boundary
        Assert.Equal(Quantize.NextBar, scheduler.LastWhen);

        scheduler.FireLast();                          // the bar boundary arrives
        VisualScene loaded = Assert.Single(_engine.LoadedScenes).Scene;
        Assert.Equal("Drop", loaded.Name);
    }

    [Fact]
    public void LoadScene_WhenQuantizeIsOff_LoadsImmediately_EvenWithASchedulerWired()
    {
        var scheduler = new CapturingBeatScheduler();
        var handler = new VisualActionHandler(_engine, scheduler: scheduler); // LaunchQuantize defaults to Immediate

        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualLoadScene, Slot: 1));

        Assert.Single(_engine.LoadedScenes);
        Assert.Null(scheduler.LastWhen);               // scheduler not used when off
    }

    [Fact]
    public void VisualSetLaunchQuantize_SetsTheMode_SoLaterLaunchesDefer()
    {
        var scheduler = new CapturingBeatScheduler();
        var handler = new VisualActionHandler(_engine, scheduler: scheduler);

        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualSetLaunchQuantize, Value: 2)); // bar
        Assert.Equal(Quantize.NextBar, handler.LaunchQuantize);

        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualLoadScene, Slot: 0));
        Assert.Empty(_engine.LoadedScenes);             // deferred, not immediate
        Assert.Equal(Quantize.NextBar, scheduler.LastWhen);
    }

    [Fact]
    public void VisualSetLaunchQuantize_Zero_TurnsQuantizeOff()
    {
        var handler = new VisualActionHandler(_engine) { LaunchQuantize = Quantize.NextBar };

        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualSetLaunchQuantize, Value: 0));

        Assert.Equal(Quantize.Immediate, handler.LaunchQuantize);
    }

    private sealed class CapturingBeatScheduler : IBeatScheduler
    {
        private Action? _last;
        public Quantize? LastWhen { get; private set; }

        public void Schedule(Quantize when, int everyN, Action onFire)
        {
            LastWhen = when;
            _last = onFire;
        }

        public void FireLast() => _last?.Invoke();
    }
}
