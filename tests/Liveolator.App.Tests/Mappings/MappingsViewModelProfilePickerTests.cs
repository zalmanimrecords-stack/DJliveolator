using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// Manual controller-profile choice (SETTINGS → MIDI mapping). Auto-selection matches a profile's hint
/// against the device's reported name; a supported controller reporting an unexpected name — behind a
/// hub, a firmware revision, a driver that decorates it — would otherwise be stranded on the empty
/// generic template with no route to its shipped map.
/// </summary>
public sealed class MappingsViewModelProfilePickerTests
{
    private static ControllerMappingProfile Profile(string name) => new(
        name, name,
        new[]
        {
            new ControllerBinding(
                MidiMessageType.NoteOn, 0, 11, PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary),
        });

    [Fact]
    public void The_picker_offers_the_sessions_catalog()
    {
        var session = new FakeSession(Profile("DDJ-400 (Pioneer DJ)"), Profile("Inpulse 300 (Hercules)"));

        var vm = new MappingsViewModel(session);

        Assert.True(vm.HasProfiles);
        Assert.Equal(new[] { "DDJ-400 (Pioneer DJ)", "Inpulse 300 (Hercules)" }, vm.Profiles.Select(p => p.Name));
    }

    [Fact]
    public async Task Applying_the_selected_profile_hands_it_to_the_session()
    {
        var chosen = Profile("DDJ-400 (Pioneer DJ)");
        var session = new FakeSession(chosen) { DeviceConnected = true };
        var vm = new MappingsViewModel(session) { SelectedProfile = chosen };

        await vm.ApplyProfileCommand.Execute().ToTask();

        Assert.Same(chosen, session.Applied);
    }

    [Fact]
    public async Task Applying_nothing_asks_for_a_choice_instead_of_clearing_the_mapping()
    {
        // Guards the destructive case: an Apply with no selection must not reach the session, or a
        // stray click would wipe a configured controller's mapping.
        var session = new FakeSession(Profile("DDJ-400 (Pioneer DJ)")) { DeviceConnected = true };
        var vm = new MappingsViewModel(session);

        await vm.ApplyProfileCommand.Execute().ToTask();

        Assert.Null(session.Applied);
        Assert.Contains("Pick a controller profile", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Applying_without_a_connected_controller_says_so()
    {
        var chosen = Profile("DDJ-400 (Pioneer DJ)");
        var session = new FakeSession(chosen) { DeviceConnected = false };
        var vm = new MappingsViewModel(session) { SelectedProfile = chosen };

        await vm.ApplyProfileCommand.Execute().ToTask();

        Assert.Contains("Connect a MIDI controller", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_session_with_no_catalog_hides_the_picker()
    {
        var vm = new MappingsViewModel(new FakeSession());

        Assert.False(vm.HasProfiles);
        Assert.Empty(vm.Profiles);
    }

    private sealed class FakeSession : IMidiControlSession
    {
        public FakeSession(params ControllerMappingProfile[] profiles) => AvailableProfiles = profiles;

        public IReadOnlyList<ControllerMappingProfile> AvailableProfiles { get; }
        public ControllerMappingProfile? Applied { get; private set; }
        public bool DeviceConnected { get; init; }

        public Task<bool> ApplyProfileAsync(
            ControllerMappingProfile profile, CancellationToken cancellationToken = default)
        {
            if (!DeviceConnected)
                return Task.FromResult(false);

            Applied = profile;
            ActiveProfile = profile;
            MappingChanged?.Invoke(this, profile);
            return Task.FromResult(true);
        }

        public ControllerMappingProfile? ActiveProfile { get; private set; }
        public bool IsLearnArmed => false;
        public bool IsInputConnected => DeviceConnected;
        public bool IsOutputConnected => false;
        public string? InputDeviceName => DeviceConnected ? "DDJ-400 MIDI 1" : null;
        public string? OutputDeviceName => null;
        public event EventHandler<ControllerMappingProfile>? MappingChanged;
        public event EventHandler? ActivityDetected { add { } remove { } }

        public Task StartAsync(Core.Settings.MidiSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public void Stop() { }
        public void BeginLearn(
            PerformanceActionKind action, int slot = 0, string? argument = null,
            ActionInputMode? preferredInputMode = null, double relativeTicksPerRevolution = 1.0,
            bool invert = false, RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement) { }
        public void CancelLearn() { }
        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
