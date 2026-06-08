using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Settings;
using Liveolator.Core.Actions;
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
        public List<AudioCaptureDevice> Devices { get; set; } = new()
        {
            new AudioCaptureDevice("0", "Line In", CaptureSourceKind.LineInput, IsDefault: true),
            new AudioCaptureDevice("1", "Loopback (Speakers)", CaptureSourceKind.SystemLoopback, IsDefault: false),
        };
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() => Devices;
    }

    private sealed class FakeMidiProvider : IMidiDeviceProvider
    {
        public List<string> Inputs { get; set; } = new() { "Ableton Push", "CMD STUDIO 2A" };
        public List<string> Outputs { get; set; } = new() { "Ableton Push" };
        public IReadOnlyList<string> GetInputDeviceNames() => Inputs;
        public IReadOnlyList<string> GetOutputDeviceNames() => Outputs;
        // The Settings VM only enumerates; opening devices is the composition root's job (not exercised here).
        public IMidiInput? OpenInput(string deviceName) => null;
        public IMidiOutput? OpenOutput(string deviceName) => null;
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

    private sealed class FakeReinitializer : IAudioEngineReinitializer
    {
        public AudioSettings? LastApplied { get; private set; }
        public bool Result { get; set; } = true;
        public bool Reinitialize(AudioSettings settings)
        {
            LastApplied = settings;
            return Result;
        }
    }

    private sealed class FakeCaptureController : ICaptureSourceController
    {
        public AudioCaptureDevice? LastSelected { get; private set; }
        public bool Called { get; private set; }
        public bool Result { get; set; } = true;
        public bool SelectCaptureSource(AudioCaptureDevice? device)
        {
            Called = true;
            LastSelected = device;
            return Result;
        }
    }

    private sealed class FakeMidiControlSession : IMidiControlSession
    {
        public MidiSettings? LastStartedWith { get; private set; }
        public ControllerMappingProfile? ActiveProfile { get; set; }
        public bool IsLearnArmed { get; private set; }
        public bool IsInputConnected { get; set; } = true;
        public string? InputDeviceName { get; set; } = "CMD STUDIO 2A";
        public bool IsOutputConnected { get; set; }
        public string? OutputDeviceName { get; set; }
        public event EventHandler? ActivityDetected
        {
            add { }
            remove { }
        }
        public event EventHandler<ControllerMappingProfile>? MappingChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default)
        {
            LastStartedWith = settings;
            return Task.CompletedTask;
        }

        public void Stop()
        {
        }

        public void BeginLearn(
            PerformanceActionKind action,
            int slot = 0,
            string? argument = null,
            ActionInputMode? preferredInputMode = null,
            double relativeTicksPerRevolution = 1.0,
            bool invert = false)
            => IsLearnArmed = true;

        public void CancelLearn() => IsLearnArmed = false;

        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static SettingsViewModel NewVm(
        FakeOutputCatalog? outputs = null,
        FakeCaptureCatalog? captures = null,
        FakeMidiProvider? midi = null,
        FakeSettingsStore? store = null,
        AudioReinitCoordinator? reinit = null,
        ICaptureSourceController? captureController = null,
        IMidiControlSession? midiControlSession = null)
        => new(outputs ?? new FakeOutputCatalog(), captures ?? new FakeCaptureCatalog(),
               midi ?? new FakeMidiProvider(), store ?? new FakeSettingsStore(),
               reinit, captureController, midiControlSession: midiControlSession);

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
    public async Task Save_ReconnectsMidiSessionWithSelectedDevices()
    {
        var session = new FakeMidiControlSession();
        var vm = NewVm(midiControlSession: session);
        vm.SelectedMidiInput = "CMD STUDIO 2A";
        vm.SelectedMidiOutput = "Ableton Push";

        await vm.SaveAsync();

        Assert.Equal("CMD STUDIO 2A", session.LastStartedWith!.ControllerInputName);
        Assert.Equal("Ableton Push", session.LastStartedWith.FeedbackOutputName);
        Assert.Contains("MIDI controller connected", vm.Status);
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

    [Fact]
    public void Construct_CaptureListLeadsWithNoneSentinel()
    {
        var vm = NewVm();

        Assert.Same(SettingsViewModel.NoCaptureSource, vm.CaptureDevices[0]);
        Assert.Contains(vm.CaptureDevices, d => d.Name == "Loopback (Speakers)");
        // Default selection is "(none)".
        Assert.Same(SettingsViewModel.NoCaptureSource, vm.SelectedCaptureDevice);
    }

    [Fact]
    public async Task Initialize_AppliesPersistedCaptureSelection()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with
            {
                Audio = new AudioSettings { CaptureDeviceId = "1", CaptureSource = CaptureSourceKind.SystemLoopback },
            },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Equal("1", vm.SelectedCaptureDevice!.Id);
    }

    [Fact]
    public async Task Initialize_PersistedCaptureGone_FallsBackToNone()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with
            {
                Audio = new AudioSettings { CaptureDeviceId = "999", CaptureSource = CaptureSourceKind.LineInput },
            },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Same(SettingsViewModel.NoCaptureSource, vm.SelectedCaptureDevice);
    }

    [Fact]
    public async Task Save_PersistsSelectedCaptureSource()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.SelectedCaptureDevice = vm.CaptureDevices.First(d => d.Id == "1");

        await vm.SaveAsync();

        Assert.Equal("1", store.Saved.Audio.CaptureDeviceId);
        Assert.Equal(CaptureSourceKind.SystemLoopback, store.Saved.Audio.CaptureSource);
    }

    [Fact]
    public async Task Save_NoCaptureSelected_PersistsNull()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.SelectedCaptureDevice = SettingsViewModel.NoCaptureSource;

        await vm.SaveAsync();

        Assert.Null(store.Saved.Audio.CaptureDeviceId);
        Assert.Null(store.Saved.Audio.CaptureSource);
    }

    [Fact]
    public async Task Save_AppliesOutputChangeToRunningEngine()
    {
        var fake = new FakeReinitializer();
        var reinit = new AudioReinitCoordinator(fake, startupSettings: AudioSettings.Default);
        var vm = NewVm(reinit: reinit);
        vm.SelectedOutputDevice = vm.OutputDevices.First(d => d.Id == "2");

        await vm.SaveAsync();

        Assert.Equal("2", fake.LastApplied!.OutputDeviceId);
        Assert.Contains("re-initialised", vm.Status);
    }

    [Fact]
    public async Task Save_RolledBackReinit_SurfacesFailureStatus()
    {
        var fake = new FakeReinitializer { Result = false };
        var reinit = new AudioReinitCoordinator(fake, startupSettings: AudioSettings.Default);
        var vm = NewVm(reinit: reinit);
        vm.SelectedOutputDevice = vm.OutputDevices.First(d => d.Id == "2");

        await vm.SaveAsync();

        Assert.Contains("kept the previous device", vm.Status);
    }

    [Fact]
    public async Task Save_AppliesSelectedCaptureSourceThroughController()
    {
        var controller = new FakeCaptureController();
        var vm = NewVm(captureController: controller);
        vm.SelectedCaptureDevice = vm.CaptureDevices.First(d => d.Id == "1");

        await vm.SaveAsync();

        Assert.True(controller.Called);
        Assert.Equal("1", controller.LastSelected!.Id);
    }

    [Fact]
    public async Task Save_NoCapture_DetachesThroughControllerWithNull()
    {
        var controller = new FakeCaptureController();
        var vm = NewVm(captureController: controller);
        vm.SelectedCaptureDevice = SettingsViewModel.NoCaptureSource;

        await vm.SaveAsync();

        Assert.True(controller.Called);
        Assert.Null(controller.LastSelected);
    }

    [Fact]
    public async Task Initialize_AppliesPersistedWaveformZoom()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with { Visuals = new VisualsSettings(WaveformZoomSeconds: 15.0) },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Equal(15.0, vm.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public async Task Save_PersistsWaveformZoom()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.WaveformZoomSeconds = 5.0;

        await vm.SaveAsync();

        Assert.Equal(5.0, store.Saved.Visuals.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public void WaveformZoomBounds_ComeFromVisualsSettings()
    {
        var vm = NewVm();

        Assert.Equal(VisualsSettings.MinZoomSeconds, vm.WaveformZoomMin, precision: 6);
        Assert.Equal(VisualsSettings.MaxZoomSeconds, vm.WaveformZoomMax, precision: 6);
    }

    [Fact]
    public async Task Initialize_AppliesPersistedNudgeSeconds()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with { Visuals = new VisualsSettings(WaveformZoomSeconds: 7.0, NudgeSeconds: 0.3) },
        };
        var vm = NewVm(store: store);

        await vm.InitializeAsync();

        Assert.Equal(0.3, vm.NudgeSeconds, precision: 6);
    }

    [Fact]
    public async Task Save_PersistsNudgeSeconds()
    {
        var store = new FakeSettingsStore();
        var vm = NewVm(store: store);
        vm.NudgeSeconds = 0.25;

        await vm.SaveAsync();

        Assert.Equal(0.25, store.Saved.Visuals.NudgeSeconds, precision: 6);
    }
}
