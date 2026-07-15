using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// The Mappings learn UI must default a jog wheel to offset-binary — its real DJ-hardware encoding —
/// instead of the generic two's-complement, which decoded the wheel's rest value (0x40) as a -64 lurch
/// so the jog "threw unrelated positions and stuck in one spot". Other relative targets keep two's-complement.
/// </summary>
public sealed class MappingsViewModelJogEncodingTests
{
    public MappingsViewModelJogEncodingTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task SelectingAJogTarget_SeedsOffsetBinary_AndForwardsItToLearn()
    {
        var session = new RecordingSession();
        var vm = new MappingsViewModel(session);

        vm.SelectedTarget = vm.Targets.First(
            t => t.Action == PerformanceActionKind.DeckJog && t.Slot == 0);

        Assert.Equal(RelativeEncoding.OffsetBinary, vm.SelectedRelativeEncoding);

        await vm.LearnCommand.Execute().ToTask();

        Assert.Equal(PerformanceActionKind.DeckJog, session.LastLearnAction);
        Assert.Equal(RelativeEncoding.OffsetBinary, session.LastLearnEncoding);
    }

    [Fact]
    public void SelectingANonJogTarget_KeepsTwosComplement()
    {
        var session = new RecordingSession();
        var vm = new MappingsViewModel(session);

        vm.SelectedTarget = vm.Targets.First(
            t => t.Action == PerformanceActionKind.DeckPlayPause);

        Assert.Equal(RelativeEncoding.TwosComplement, vm.SelectedRelativeEncoding);
    }

    private sealed class RecordingSession : IMidiControlSession
    {
        public PerformanceActionKind? LastLearnAction { get; private set; }
        public RelativeEncoding LastLearnEncoding { get; private set; }

        public ControllerMappingProfile? ActiveProfile => null;
        public bool IsLearnArmed => false;
        public bool IsInputConnected => true;
        public string? InputDeviceName => "CMD Studio 2A";
        public bool IsOutputConnected => false;
        public string? OutputDeviceName => null;

        public event EventHandler<ControllerMappingProfile>? MappingChanged { add { } remove { } }
        public event EventHandler? ActivityDetected { add { } remove { } }

        public Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Stop() { }

        public void BeginLearn(
            PerformanceActionKind action,
            int slot = 0,
            string? argument = null,
            ActionInputMode? preferredInputMode = null,
            double relativeTicksPerRevolution = 1.0,
            bool invert = false,
            RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement)
        {
            LastLearnAction = action;
            LastLearnEncoding = relativeEncoding;
        }

        public void CancelLearn() { }

        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
