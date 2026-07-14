using System.Collections.Generic;
using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class EqCutModeKnobViewModelTests
{
    [Theory]
    [InlineData(EqCutMode.Eq, 0.0)]
    [InlineData(EqCutMode.Deep, 0.5)]
    [InlineData(EqCutMode.Kill, 1.0)]
    public void ToValue_MapsEachModeToItsDetent(EqCutMode mode, double expected)
        => Assert.Equal(expected, EqCutModeKnobViewModel.ToValue(mode));

    [Fact]
    public void InitialMode_SeedsValueAndLabel()
    {
        var vm = new EqCutModeKnobViewModel(EqCutMode.Deep, onUserChanged: null);

        Assert.Equal(0.5, vm.Value);
        Assert.Equal("DEEP", vm.ModeLabel);
        Assert.Equal(EqCutMode.Deep, vm.Mode);
    }

    [Theory]
    [InlineData(0.0, EqCutMode.Eq)]
    [InlineData(0.5, EqCutMode.Deep)]
    [InlineData(1.0, EqCutMode.Kill)]
    public void UserTurnToDetent_EmitsThatMode(double value, EqCutMode expected)
    {
        var emitted = new List<EqCutMode>();
        var vm = new EqCutModeKnobViewModel(EqCutMode.Kill, emitted.Add) { Value = 0.0 };
        emitted.Clear();

        vm.Value = value;

        Assert.Equal(expected, vm.Mode);
        if (expected == EqCutMode.Eq)
            Assert.Empty(emitted); // already at Eq from the seed turn
        else
            Assert.Equal(expected, Assert.Single(emitted));
    }

    [Theory]
    [InlineData(0.2, EqCutMode.Eq, 0.0)]   // below the EQ/DEEP midpoint snaps down to EQ
    [InlineData(0.3, EqCutMode.Deep, 0.5)] // above it snaps up to DEEP
    [InlineData(0.8, EqCutMode.Kill, 1.0)] // above the DEEP/KILL midpoint snaps to KILL
    public void Value_SnapsToNearestDetent(double raw, EqCutMode expectedMode, double expectedValue)
    {
        var vm = new EqCutModeKnobViewModel(EqCutMode.Eq, _ => { });

        vm.Value = raw;

        Assert.Equal(expectedValue, vm.Value);
        Assert.Equal(expectedMode, vm.Mode);
    }

    [Fact]
    public void UserTurn_WithinSameDetent_DoesNotReEmit()
    {
        var emitted = new List<EqCutMode>();
        var vm = new EqCutModeKnobViewModel(EqCutMode.Eq, emitted.Add);

        vm.Value = 0.1; // still snaps to Eq
        vm.Value = 0.2; // still Eq

        Assert.Empty(emitted);
    }

    [Fact]
    public void SetFromMode_UpdatesValueAndLabel_WithoutEmitting()
    {
        var emitted = new List<EqCutMode>();
        var vm = new EqCutModeKnobViewModel(EqCutMode.Kill, emitted.Add);

        vm.SetFromMode(EqCutMode.Eq);

        Assert.Equal(0.0, vm.Value);
        Assert.Equal("EQ", vm.ModeLabel);
        Assert.Empty(emitted);
    }

    [Fact]
    public void NullCallback_DisablesKnob_AndNeverEmits()
    {
        var vm = new EqCutModeKnobViewModel(EqCutMode.Eq, onUserChanged: null);

        Assert.False(vm.IsEnabled);

        vm.Value = 1.0; // must not throw despite the null callback

        Assert.Equal(EqCutMode.Kill, vm.Mode);
    }
}
