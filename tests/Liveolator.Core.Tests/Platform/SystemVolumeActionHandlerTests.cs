using Liveolator.Core.Actions;
using Liveolator.Core.Platform;
using Xunit;

namespace Liveolator.Core.Tests.Platform;

public class SystemVolumeActionHandlerTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void HandledKinds_IsOnlySystemMasterVolume()
    {
        var handler = new SystemVolumeActionHandler(new FakeSystemVolumeController());

        Assert.Equal(new[] { PerformanceActionKind.SystemMasterVolume }, handler.HandledKinds);
    }

    [Fact]
    public void Absolute_SetsControllerVolume()
    {
        var controller = new FakeSystemVolumeController(initial: 0.2);
        var handler = new SystemVolumeActionHandler(controller);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: 0.7));

        Assert.Equal(0.7, controller.GetVolume(), Tol);
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.3, 0.0)]
    public void Absolute_ClampsToUnitRange(double requested, double expected)
    {
        var controller = new FakeSystemVolumeController();
        var handler = new SystemVolumeActionHandler(controller);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: requested));

        Assert.Equal(expected, controller.GetVolume(), Tol);
    }

    [Fact]
    public void Relative_AddsDeltaToCurrentVolume()
    {
        var controller = new FakeSystemVolumeController(initial: 0.4);
        var handler = new SystemVolumeActionHandler(controller);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Relative, Value: 0.25));

        Assert.Equal(0.65, controller.GetVolume(), Tol);
    }

    [Fact]
    public void Handle_RaisesValueFeedbackForUiAndMidi()
    {
        var handler = new SystemVolumeActionHandler(new FakeSystemVolumeController());
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: 0.33));

        Assert.NotNull(feedback);
        Assert.Equal(PerformanceActionKind.SystemMasterVolume, feedback!.Kind);
        Assert.True(feedback.State.IsAvailable);
        Assert.Equal(0.33, feedback.State.Value, Tol);
    }

    [Fact]
    public void GetFeedback_ReportsCurrentControllerVolume()
    {
        var controller = new FakeSystemVolumeController(initial: 0.6);
        var handler = new SystemVolumeActionHandler(controller);

        ActionFeedbackState state = handler.GetFeedback(PerformanceActionKind.SystemMasterVolume, slot: 0);

        Assert.True(state.IsAvailable);
        Assert.Equal(0.6, state.Value, Tol);
    }

    [Fact]
    public void UnavailableController_DoesNotWriteAndReportsUnavailable()
    {
        var controller = new FakeSystemVolumeController(available: false);
        var handler = new SystemVolumeActionHandler(controller);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: 0.9));

        Assert.Equal(0, controller.SetCount);
        Assert.False(feedback!.State.IsAvailable);
        Assert.False(handler.GetFeedback(PerformanceActionKind.SystemMasterVolume, 0).IsAvailable);
    }

    [Fact]
    public void SetFailure_IsSwallowedAndReportsLastKnownLevel()
    {
        var controller = new FakeSystemVolumeController(initial: 0.5) { ThrowOnSet = true };
        var handler = new SystemVolumeActionHandler(controller);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        Exception? thrown = Record.Exception(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: 0.8)));

        Assert.Null(thrown);
        // Write failed, so feedback reflects the unchanged last-known level, not the requested one.
        Assert.True(feedback!.State.IsAvailable);
        Assert.Equal(0.5, feedback.State.Value, Tol);
    }
}
