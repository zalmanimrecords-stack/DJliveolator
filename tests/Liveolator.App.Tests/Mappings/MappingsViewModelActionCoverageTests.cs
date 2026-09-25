using Liveolator.App.Composition;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// The MAPPINGS screen is the only route from a hardware control to an action. These tests assert the
/// route exists for EVERY declared <see cref="PerformanceActionKind"/>, so a new kind cannot ship
/// handler-only and invisible (doc core-business-logic/06 recorded 14 kinds in exactly that state).
/// </summary>
public sealed class MappingsViewModelActionCoverageTests
{
    private static MappingsViewModel NewViewModel() => new(new StubMidiControlSession());

    [Fact]
    public void EveryBindableActionKind_HasALearnTarget()
    {
        using MappingsViewModel vm = NewViewModel();
        HashSet<PerformanceActionKind> offered = vm.Targets.Select(target => target.Action).ToHashSet();

        PerformanceActionKind[] missing = Enum.GetValues<PerformanceActionKind>()
            .Where(kind => !ActionTargetVocabulary.NotBindable.Contains(kind))
            .Where(kind => StemsFeature.IsEnabled || !StemsFeature.IsStemAction(kind))
            .Where(kind => !offered.Contains(kind))
            .ToArray();

        Assert.Empty(missing);
    }

    // The gap doc 06 named: a handler existed but nothing could reach it from the UI.
    [Theory]
    [InlineData(PerformanceActionKind.MixerEqKill)]
    [InlineData(PerformanceActionKind.DeckCuePlay)]
    [InlineData(PerformanceActionKind.DeckLoopHalve)]
    [InlineData(PerformanceActionKind.DeckLoopDouble)]
    [InlineData(PerformanceActionKind.DeckHotCueClear)]
    [InlineData(PerformanceActionKind.DeckQuantizeToggle)]
    public void PreviouslyUnreachableKinds_AreNowLearnable(PerformanceActionKind kind)
    {
        using MappingsViewModel vm = NewViewModel();

        Assert.Contains(vm.Targets, target => target.Action == kind);
    }

    [Fact]
    public void GeneratedPerDeckKinds_OfferOneTargetPerDeck()
    {
        using MappingsViewModel vm = NewViewModel();

        Assert.Single(vm.Targets, t => t.Action == PerformanceActionKind.DeckLoopHalve && t.Slot == 0);
        Assert.Single(vm.Targets, t => t.Action == PerformanceActionKind.DeckLoopHalve && t.Slot == 1);
    }

    [Fact]
    public void GeneratedArgumentKinds_OfferOneTargetPerArgumentValue()
    {
        using MappingsViewModel vm = NewViewModel();

        foreach (string band in new[] { "Low", "Mid", "High" })
            Assert.Single(vm.Targets,
                t => t.Action == PerformanceActionKind.MixerEqKill && t.Slot == 0 && t.Argument == band);
    }

    // The hand-written entries are the authority wherever they exist: generation must not duplicate them
    // or lose the hardware knowledge they carry.
    [Fact]
    public void HandWrittenTargets_KeepTheirLabelsAndHardwareEncoding()
    {
        using MappingsViewModel vm = NewViewModel();

        MappingTargetViewModel jog = Assert.Single(vm.Targets,
            t => t.Action == PerformanceActionKind.DeckJog && t.Slot == 0);
        Assert.Equal("Deck A: Jog / track position", jog.Label);
        Assert.Equal(ActionInputMode.Relative, jog.PreferredInputMode);
        Assert.Equal(RelativeEncoding.OffsetBinary, jog.RelativeEncoding);
        Assert.Equal(128.0, jog.RelativeTicksPerRevolution);

        Assert.Single(vm.Targets, t => t.Action == PerformanceActionKind.DeckPlayPause && t.Slot == 0);
        Assert.Equal("Deck A: Play / Pause", vm.Targets[0].Label);
    }

    [Fact]
    public void ExcludedKinds_AreNotOffered()
    {
        using MappingsViewModel vm = NewViewModel();

        foreach (PerformanceActionKind kind in ActionTargetVocabulary.NotBindable)
            Assert.DoesNotContain(vm.Targets, target => target.Action == kind);
    }

    // Stems are shelved (StemsFeature): their learn targets stay hidden unless LIVEOLATOR_STEMS=1.
    [Fact]
    public void StemTargets_AreHidden_WhileStemsAreShelved()
    {
        if (StemsFeature.IsEnabled)
            return; // a developer run with LIVEOLATOR_STEMS=1 offers them on purpose
        using MappingsViewModel vm = NewViewModel();

        Assert.DoesNotContain(vm.Targets, target => StemsFeature.IsStemAction(target.Action));
    }

    private sealed class StubMidiControlSession : IMidiControlSession
    {
        public ControllerMappingProfile? ActiveProfile => null;
        public bool IsLearnArmed => false;
        public bool IsInputConnected => false;
        public string? InputDeviceName => "Stub";
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
        }

        public void CancelLearn() { }

        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
