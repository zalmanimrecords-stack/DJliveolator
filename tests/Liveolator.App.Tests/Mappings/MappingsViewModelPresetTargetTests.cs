using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// The MAPPINGS tab exposes one learn target per controllable preset parameter (doc 28), so a hardware
/// knob can be bound to e.g. GLOW via the existing MIDI-learn flow (the binding carries the namespaced
/// macro name as its Argument).
/// </summary>
public sealed class MappingsViewModelPresetTargetTests
{
    private const string PresetId = "com.example.vis/aurora";

    public MappingsViewModelPresetTargetTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    private static GeneratorPresetRegistry PresetsWith(params string[] controllableIds)
    {
        var registry = new GeneratorPresetRegistry();
        registry.ReplacePackage("com.example.vis", new[]
        {
            new GeneratorPreset(PresetId, "Aurora", "com.example.vis/gen", "1.0.0",
                controllableIds.Select(id => new ControllableParameter(id, id.ToUpperInvariant())).ToArray()),
        });
        return registry;
    }

    [Fact]
    public void Targets_IncludeOneEntryPerControllablePresetParameter()
    {
        var vm = new MappingsViewModel(new FakeMidiControlSession(), PresetsWith("glow", "warp"));

        MappingTargetViewModel glow = Assert.Single(
            vm.Targets, t => t.Argument == $"{PresetId}.glow");
        Assert.Equal(PerformanceActionKind.VisualSetMacro, glow.Action);
        Assert.Equal("Visuals: Aurora - GLOW", glow.Label);
        Assert.Contains(vm.Targets, t => t.Argument == $"{PresetId}.warp");
    }

    [Fact]
    public void NoRegistry_LeavesOnlyTheFixedTargets()
    {
        var vm = new MappingsViewModel(new FakeMidiControlSession());
        Assert.DoesNotContain(vm.Targets, t => t.Action == PerformanceActionKind.VisualSetMacro);
        Assert.Contains(vm.Targets, t => t.Action == PerformanceActionKind.VisualBlackout); // fixed targets remain
    }

    [Fact]
    public async Task Learning_APresetTarget_ArmsTheSessionWithTheMacroNameAsArgument()
    {
        var session = new FakeMidiControlSession();
        var vm = new MappingsViewModel(session, PresetsWith("glow"));
        vm.SelectedTarget = vm.Targets.First(t => t.Argument == $"{PresetId}.glow");

        await vm.LearnCommand.Execute().ToTask();

        Assert.Equal(PerformanceActionKind.VisualSetMacro, session.LearnedAction);
        Assert.Equal($"{PresetId}.glow", session.LearnedArgument);
    }

    private sealed class FakeMidiControlSession : IMidiControlSession
    {
        public PerformanceActionKind? LearnedAction { get; private set; }
        public string? LearnedArgument { get; private set; }

        public ControllerMappingProfile? ActiveProfile => null;
        public bool IsLearnArmed { get; private set; }
        public bool IsInputConnected => false;
        public string? InputDeviceName => "Fake";
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
            bool invert = false)
        {
            LearnedAction = action;
            LearnedArgument = argument;
            IsLearnArmed = true;
        }

        public void CancelLearn() => IsLearnArmed = false;

        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
