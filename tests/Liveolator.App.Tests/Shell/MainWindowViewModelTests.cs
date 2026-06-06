using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Settings;
using Liveolator.App.Shell;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Audio;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
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

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(AppSettings.Default);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static MainWindowViewModel BuildShell()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var status = new ShellStatusViewModel(
            new FakeMidiStatus(), new FakeOutputCatalog(), AppSettings.Default, new HistoricalScheduler());
        var settings = new SettingsViewModel(
            new FakeOutputCatalog(), new FakeCaptureCatalog(), new FakeMidiProvider(), new FakeSettingsStore());

        return new MainWindowViewModel(
            new LibrariesViewModel(library), new LiveViewModel(), new DjViewModel(), settings, status);
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
}
