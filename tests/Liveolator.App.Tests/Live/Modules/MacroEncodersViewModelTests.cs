using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using System.Reactive.Concurrency;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class MacroEncodersViewModelTests
{
    public MacroEncodersViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public void Exposes_Eight_Encoders_InMockOrder()
    {
        var vm = new MacroEncodersViewModel(new FakeDispatcher());

        Assert.Equal(8, vm.Encoders.Count);
        Assert.Equal("Intensity", vm.Encoders[0].Label);
        Assert.Equal("Opacity", vm.Encoders[7].Label);
    }

    [Fact]
    public void Encoder_EmitsVisualSetMacro_WithMacroNameAndValue()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MacroEncodersViewModel(dispatcher);

        vm.Encoders[0].Value = 0.5;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetMacro, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal("intensity", action.Argument);
        Assert.Equal(0.5, action.Value);
    }

    [Fact]
    public void NoDispatcher_DisablesEncoders()
    {
        var vm = new MacroEncodersViewModel();

        Assert.False(vm.IsEnabled);
        Assert.All(vm.Encoders, e => Assert.False(e.IsEnabled));
    }

    [Fact]
    public void MacroFeedback_UpdatesMatchingEncoderWithoutRedispatch()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MacroEncodersViewModel(dispatcher);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.VisualSetMacro,
            slot: 0,
            new ActionFeedbackState(false, true, 0.25, "echo"));

        Assert.Equal(0.25, vm.Encoders[2].Value);
        Assert.Empty(dispatcher.Dispatched);
    }
}
