using Liveolator.App.Features.Live.Modules;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class ContinuousControlViewModelTests
{
    [Fact]
    public void UserChange_EmitsThroughCallback()
    {
        double? emitted = null;
        var control = new ContinuousControlViewModel("Hi", initial: 0.5, v => emitted = v);

        control.Value = 0.8;

        Assert.Equal(0.8, emitted);
        Assert.Equal(0.8, control.Value);
        Assert.True(control.IsEnabled);
    }

    [Fact]
    public void SetFromFeedback_UpdatesValue_WithoutEmitting()
    {
        int emitCount = 0;
        var control = new ContinuousControlViewModel("Hi", initial: 0.5, _ => emitCount++);

        control.SetFromFeedback(0.3);

        Assert.Equal(0.3, control.Value);
        Assert.Equal(0, emitCount);
    }

    [Fact]
    public void NullCallback_DisablesControl_AndNeverEmits()
    {
        var control = new ContinuousControlViewModel("Pitch", initial: 0.5, onUserChanged: null);

        control.Value = 0.9;

        Assert.False(control.IsEnabled);
        Assert.Equal(0.9, control.Value); // still bindable, just inert
    }
}
