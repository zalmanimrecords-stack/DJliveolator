using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Addons;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Mappings;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Studio;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Shell;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Audio;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Studio;
using Liveolator.App.Tests.Live;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.App.Tests.Shell;

/// <summary>
/// Verifies the shell's tab navigation: <see cref="MainWindowViewModel.SelectNextTab"/> /
/// <see cref="MainWindowViewModel.SelectPreviousTab"/> (driven by Tab / Shift+Tab) step through the
/// tab set in order and wrap around at either end.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private sealed class FakeOutputCatalog : IAudioOutputDeviceCatalog
    {
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() => Array.Empty<AudioOutputDevice>();
    }

    private sealed class FakeCaptureCatalog : IAudioCaptureDeviceCatalog
    {
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() => Array.Empty<AudioCaptureDevice>();
    }

    private sealed class FakeMidiProvider : IMidiDeviceProvider
    {
        public IReadOnlyList<string> GetInputDeviceNames() => Array.Empty<string>();
        public IReadOnlyList<string> GetOutputDeviceNames() => Array.Empty<string>();
        public IMidiInput? OpenInput(string deviceName) => null;
        public IMidiOutput? OpenOutput(string deviceName) => null;
    }

    private sealed class FakeMidiStatus : IMidiControlStatus
    {
        public bool IsInputConnected => false;
        public string? InputDeviceName => null;
        public bool IsOutputConnected => false;
        public string? OutputDeviceName => null;
        public event EventHandler? ActivityDetected { add { } remove { } }
    }

    private sealed class FakeMidiControlSession : IMidiControlSession
    {
        public ControllerMappingProfile? ActiveProfile => null;
        public bool IsLearnArmed => false;
        public bool IsInputConnected => false;
        public string? InputDeviceName => null;
        public bool IsOutputConnected => false;
        public string? OutputDeviceName => null;
        public event EventHandler? ActivityDetected { add { } remove { } }
        public event EventHandler<ControllerMappingProfile>? MappingChanged { add { } remove { } }
        public Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public void Stop() { }
        public void BeginLearn(
            Liveolator.Core.Actions.PerformanceActionKind action,
            int slot = 0,
            string? argument = null,
            Liveolator.Core.Actions.ActionInputMode? preferredInputMode = null,
            double relativeTicksPerRevolution = 1.0,
            bool invert = false,
            Liveolator.Core.Mapping.RelativeEncoding relativeEncoding = Liveolator.Core.Mapping.RelativeEncoding.TwosComplement) { }
        public void CancelLearn() { }
        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(AppSettings.Default);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeStudioProjectStore : IStudioProjectStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<StudioProject?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult<StudioProject?>(null);
        public Task SaveAsync(StudioProject project, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static MainWindowViewModel BuildShell(
        AppSettings? appSettings = null, AudioEngineStatus? audioStatus = null, DjViewModel? dj = null)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var status = new ShellStatusViewModel(
            new FakeMidiStatus(), new FakeOutputCatalog(), AppSettings.Default, new HistoricalScheduler());
        var settings = new SettingsViewModel(
            new FakeOutputCatalog(), new FakeCaptureCatalog(), new FakeMidiProvider(), new FakeSettingsStore());
        var visualLibrary = new VisualLibraryViewModel(
            new VisualMediaLibrary(new FakeFileEnumerator(), new FakeVisualMediaProbe()));
        var addons = new AddonsViewModel(
            new FakeSettingsStore(), VuMeterAddon.FaceSpec, _ => "default-face.png");
        var midiLearn = new GlobalMidiLearnCoordinator(new FakeMidiControlSession());

        var studio = new StudioViewModel(library, new FakeStudioProjectStore());

        return new MainWindowViewModel(
            new LibrariesViewModel(library), new LiveViewModel(), dj ?? new DjViewModel(),
            studio, visualLibrary, addons, settings, midiLearn, status,
            new SystemVolumeControlViewModel(), appSettings, audioStatus);
    }

    [Fact]
    public void AudioEngineWarning_IsHidden_WhenTheEngineIsHealthy()
    {
        var vm = BuildShell(audioStatus: AudioEngineStatus.Healthy);

        Assert.False(vm.HasAudioEngineWarning);
        Assert.Null(vm.AudioEngineWarning);
    }

    [Fact]
    public void AudioEngineWarning_IsHidden_WhenNoStatusIsProvided()
    {
        var vm = BuildShell();

        Assert.False(vm.HasAudioEngineWarning);
    }

    [Fact]
    public void AudioEngineWarning_IsShown_WhenTheEffectsLibraryIsMissing()
    {
        // The bass_fx-missing case: the engine exists but every track load would fail — the shell must
        // state it up front so the dead decks aren't a mystery (the owner's silent-SYNC report).
        var status = new AudioEngineStatus(
            PlaybackAvailable: true, EffectsAvailable: false, Warning: "bass_fx is missing — tracks can't load.");

        var vm = BuildShell(audioStatus: status);

        Assert.True(vm.HasAudioEngineWarning);
        Assert.Equal("bass_fx is missing — tracks can't load.", vm.AudioEngineWarning);
    }

    [Fact]
    public void Limiter_ExposesTheDjMixer_SoTheTopBarCanBindIt()
    {
        // The master smart-limiter controls were moved from the DJ mixer frame to the global top bar,
        // which binds to MainWindowViewModel — so the shell must surface the DJ mixer (the only one with a
        // limiter) as the binding source.
        var dj = new DjViewModel();

        var vm = BuildShell(dj: dj);

        Assert.Same(dj.Mixer, vm.Limiter);
    }

    [Fact]
    public void SelectNextTab_AdvancesInOrderThenWrapsToFirst()
    {
        var vm = BuildShell();
        var first = vm.Tabs[0];
        Assert.Same(first, vm.CurrentTab);

        for (int i = 1; i < vm.Tabs.Count; i++)
        {
            vm.SelectNextTab();
            Assert.Same(vm.Tabs[i], vm.CurrentTab);
        }

        vm.SelectNextTab();
        Assert.Same(first, vm.CurrentTab);
    }

    [Fact]
    public void SelectPreviousTab_FromFirstWrapsToLast_ThenStepsBack()
    {
        var vm = BuildShell();

        vm.SelectPreviousTab();
        Assert.Same(vm.Tabs[^1], vm.CurrentTab);

        vm.SelectPreviousTab();
        Assert.Same(vm.Tabs[^2], vm.CurrentTab);
    }

    [Fact]
    public void Constructor_RestoresThePersistedActiveTab()
    {
        var settings = AppSettings.Default with
        {
            WindowLayout = new WindowLayoutSettings(ActiveTabId: "DJ"),
        };

        var vm = BuildShell(settings);

        Assert.Equal("DJ", vm.CurrentTab.Title);
        Assert.Equal("DJ", vm.CurrentTabId);
    }

    [Fact]
    public void Constructor_UnknownPersistedTab_FallsBackToFirstTab()
    {
        var settings = AppSettings.Default with
        {
            WindowLayout = new WindowLayoutSettings(ActiveTabId: "NONEXISTENT"),
        };

        var vm = BuildShell(settings);

        Assert.Same(vm.Tabs[0], vm.CurrentTab);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SelectTabByNumber_SelectsTheTabAtThatPosition(int number)
    {
        var vm = BuildShell();

        vm.SelectTabByNumber(number);

        Assert.Same(vm.Tabs[number - 1], vm.CurrentTab);
    }

    [Theory]
    [InlineData(0)]                 // below the first tab
    [InlineData(-3)]                // negative
    [InlineData(int.MaxValue)]      // far past the last tab
    public void SelectTabByNumber_OutOfRange_LeavesSelectionUnchanged(int number)
    {
        var vm = BuildShell();
        var before = vm.CurrentTab;

        vm.SelectTabByNumber(number);

        Assert.Same(before, vm.CurrentTab);
    }
}
