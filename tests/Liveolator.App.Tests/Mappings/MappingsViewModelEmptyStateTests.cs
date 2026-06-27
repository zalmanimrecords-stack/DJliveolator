using System;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// The bindings list exposes an <see cref="MappingsViewModel.IsEmpty"/> flag so the View can render an
/// in-list empty-state placeholder. It is true when the profile has no bindings AND when no profile/device
/// is connected, and false once any binding exists.
/// </summary>
public sealed class MappingsViewModelEmptyStateTests
{
    [Fact]
    public void EmptyProfile_IsEmptyIsTrue()
    {
        var session = new StubSession(GenericControllerProfile.Default, deviceName: "Acme Beat Pad");

        var vm = new MappingsViewModel(session);

        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void NoProfile_IsEmptyIsTrue()
    {
        var session = new StubSession(activeProfile: null, deviceName: null);

        var vm = new MappingsViewModel(session);

        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void NonEmptyProfile_IsEmptyIsFalse()
    {
        var profile = ControllerMappingProfile.Empty("CMD", "CMD Studio").WithBinding(
            new ControllerBinding(
                MidiMessageType.NoteOn, 0, 0x3B, PerformanceActionKind.DeckPlayPause,
                ActionInputMode.Momentary, 0));
        var session = new StubSession(profile, deviceName: "CMD Studio 2A");

        var vm = new MappingsViewModel(session);

        Assert.False(vm.IsEmpty);
    }

    private sealed class StubSession : IMidiControlSession
    {
        private readonly string? _deviceName;

        public StubSession(ControllerMappingProfile? activeProfile, string? deviceName)
        {
            ActiveProfile = activeProfile;
            _deviceName = deviceName;
        }

        public ControllerMappingProfile? ActiveProfile { get; }
        public bool IsLearnArmed => false;
        public bool IsInputConnected => _deviceName is not null;
        public string? InputDeviceName => _deviceName;
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
        { }

        public void CancelLearn() { }

        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
