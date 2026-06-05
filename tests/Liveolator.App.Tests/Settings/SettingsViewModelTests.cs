using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Settings;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Settings;

/// <summary>
/// Verifies the Settings view-model: it detects audio output + MIDI equipment through the Core seams,
/// applies and persists the user's choices (output device, buffer, controller) via the settings store,
/// and degrades cleanly when a previously-selected device is gone.
/// </summary>
public sealed class SettingsViewModelTests
{
    public SettingsViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakeOutputCatalog : IAudioOutputDeviceCatalog
    {
        public List<AudioOutputDevice> Devices { get; set; } = new()
        {
            new AudioOutputDevice("1", "Speakers", IsDefault: true),
            new AudioOutputDevice("2", "CMD STUDIO 2A", IsDefault: false),
        };
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() => Devices;
    }

    private sealed class FakeCaptureCatalog : IAudioCaptureDeviceCatalog
    {
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() => new[]
        {
            new AudioCaptureDevice("0", "Line In", CaptureSourceKind.LineInput, IsDefault: true),
        };
    }

    private sealed class FakeMidiProvider : IMidiDeviceProvider
    {
        public List<string> Inputs { get; set; } = new() { "Ableton Push", "CMD STUDIO 2A" };
        public List<string> Outputs { get; set; } = new() { "Ableton Push" };
        public IReadOnlyList<string> GetInputDeviceNames() => Inputs;
        public IReadOnlyList<string> GetOutputDeviceNames() => Outputs;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Saved { get; set; } = AppSettings.Default;
        public AppSettings ToLoad { get; set; } = AppSettings.Default;
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(ToLoad);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private static SettingsViewModel NewVm(
        FakeOutputCatalog? outputs = null,
        FakeMidiProvider? midi = null,
        FakeSettingsStore? store = null)
        => new(outputs ?? new FakeOutputCatalog(), new FakeCaptureCatalog(),
               midi ?? new FakeMidiProvider(), store ?? new FakeSettingsStore());

    [Fact]
    public void Construct_PopulatesDeviceListsWithSentinels()
    {
        var vm = NewVm();

        // Output list leads with a "system default" entry, then the enumerated devices.
        Assert.Same(SettingsViewModel.SystemDefaultOutput, vm.OutputDevices[0]);
        Assert.Contains(vm.OutputDevices, d => d.Name == "CMD STUDIO 2A");

        // MIDI lists lead with a "(none)" entry.
        Assert.Equal(SettingsViewModel.NoDevice, vm.MidiInputDevices[0]);
        Assert.Contains("Ableton Push", vm.MidiInputDevices);

        Assert.Contains(AudioSettings.DefaultBufferMs, vm.BufferOptions);
        Assert.Contains(vm.CaptureDevices, d => d.Name == "Line In");
    }

    [Fact]
    public async Task Initialize_AppliesPersistedSelections()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = new AppSettings
            {
                Audio = new AudioSettings { OutputDeviceId = "2", BufferMilliseconds = 60 },
                Midi = new MidiSettings { ControllerInputName = "Ableton Push", FeedbackOutputName = "Ableton Push" },
            },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Equal("2", vm.SelectedOutputDevice!.Id);
        Assert.Equal(60, vm.SelectedBufferMs);
        Assert.Equal("Ableton Push", vm.SelectedMidiInput);
        Assert.Equal("Ableton Push", vm.SelectedMidiOutput);
    }

    [Fact]
    public async Task Initialize_PersistedDeviceGone_FallsBackToDefaults()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = new AppSettings
            {
                Audio = new AudioSettings { OutputDeviceId = "999", BufferMilliseconds = 40 },
                Midi = new MidiSettings { ControllerInputName = "Unplugged Controller" },
            },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Same(SettingsViewModel.SystemDefaultOutput, vm.SelectedOutputDevice);
        Assert.Equal(SettingsViewModel.NoDevice, vm.SelectedMidiInput);
    }

    [Fact]
    public async Task Initialize_PersistedNonPresetBuffer_BecomesSelectable()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with { Audio = new AudioSettings { BufferMilliseconds = 33 } },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Equal(33, vm.SelectedBufferMs);
        Assert.Contains(33, vm.BufferOptions);
    }

    [Fact]
    public void RefreshDevices_ReenumeratesHotPluggedEquipment()
    {
        var outputs = new FakeOutputCatalog();
        var vm = NewVm(outputs: outputs);

        outputs.Devices = new List<AudioOutputDevice> { new("5", "New Interface", IsDefault: true) };
        vm.RefreshDevices();

        Assert.Contains(vm.OutputDevices, d => d.Name == "New Interface");
        Assert.DoesNotContain(vm.OutputDevices, d => d.Name == "Speakers");
    }

    [Fact]
    public async Task Save_PersistsNormalizedSelections()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.SelectedOutputDevice = vm.OutputDevices.First(d => d.Id == "2");
        vm.SelectedBufferMs = 100;
        vm.SelectedMidiInput = "CMD STUDIO 2A";
        vm.SelectedMidiOutput = "Ableton Push";

        await vm.SaveAsync();

        Assert.Equal("2", store.Saved.Audio.OutputDeviceId);
        Assert.Equal(100, store.Saved.Audio.BufferMilliseconds);
        Assert.Equal("CMD STUDIO 2A", store.Saved.Midi.ControllerInputName);
        Assert.Equal("Ableton Push", store.Saved.Midi.FeedbackOutputName);
    }

    [Fact]
    public async Task Save_SystemDefaultAndNone_PersistAsNull()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.SelectedOutputDevice = SettingsViewModel.SystemDefaultOutput;
        vm.SelectedMidiInput = SettingsViewModel.NoDevice;
        vm.SelectedMidiOutput = SettingsViewModel.NoDevice;

        await vm.SaveAsync();

        Assert.Null(store.Saved.Audio.OutputDeviceId);
        Assert.Null(store.Saved.Midi.ControllerInputName);
        Assert.Null(store.Saved.Midi.FeedbackOutputName);
    }
}
