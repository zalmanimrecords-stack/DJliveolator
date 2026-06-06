using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Shell;

/// <summary>
/// Verifies the top-bar status view-model: it reports where audio is routed and which MIDI device is
/// connected, and flashes the MIDI indicator (<see cref="ShellStatusViewModel.MidiActive"/>) green for
/// a short window on each incoming message, then clears it after a quiet gap.
/// </summary>
public sealed class ShellStatusViewModelTests
{
    private sealed class FakeOutputCatalog : IAudioOutputDeviceCatalog
    {
        public List<AudioOutputDevice> Devices { get; set; } = new()
        {
            new AudioOutputDevice("1", "Speakers", IsDefault: true),
            new AudioOutputDevice("2", "CMD STUDIO 2A", IsDefault: false),
        };
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() => Devices;
    }

    private sealed class FakeMidiStatus : IMidiControlStatus
    {
        public bool IsInputConnected { get; set; }
        public string? InputDeviceName { get; set; }
        public bool IsOutputConnected { get; set; }
        public string? OutputDeviceName { get; set; }
        public event EventHandler? ActivityDetected;
        public void Emit() => ActivityDetected?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings SettingsWith(string? outputId, string? controller, string? feedback)
        => AppSettings.Default with
        {
            Audio = new AudioSettings { OutputDeviceId = outputId },
            Midi = new MidiSettings { ControllerInputName = controller, FeedbackOutputName = feedback },
        };

    [Fact]
    public void AudioOutputName_ResolvesSelectedDeviceFromCatalog()
    {
        var vm = new ShellStatusViewModel(
            new FakeMidiStatus(), new FakeOutputCatalog(), SettingsWith("2", null, null), new HistoricalScheduler());

        Assert.Equal("CMD STUDIO 2A", vm.AudioOutputName);
    }

    [Fact]
    public void AudioOutputName_FallsBackToSystemDefault_WhenNoneOrUnknown()
    {
        var none = new ShellStatusViewModel(
            new FakeMidiStatus(), new FakeOutputCatalog(), SettingsWith(null, null, null), new HistoricalScheduler());
        var stale = new ShellStatusViewModel(
            new FakeMidiStatus(), new FakeOutputCatalog(), SettingsWith("999", null, null), new HistoricalScheduler());

        Assert.Equal("System default", none.AudioOutputName);
        Assert.Equal("System default", stale.AudioOutputName);
    }

    [Fact]
    public void MidiNames_PreferConnectedDevice_ThenFallBackToConfiguredName()
    {
        var connected = new ShellStatusViewModel(
            new FakeMidiStatus { IsInputConnected = true, InputDeviceName = "Ableton Push 1" },
            new FakeOutputCatalog(), SettingsWith(null, "Push", null), new HistoricalScheduler());
        var configuredOnly = new ShellStatusViewModel(
            new FakeMidiStatus { IsInputConnected = false },
            new FakeOutputCatalog(), SettingsWith(null, "CMD STUDIO 2A", null), new HistoricalScheduler());

        Assert.Equal("Ableton Push 1", connected.MidiInputName);
        Assert.True(connected.MidiInputConnected);
        Assert.Equal("CMD STUDIO 2A", configuredOnly.MidiInputName);
        Assert.False(configuredOnly.MidiInputConnected);
    }

    [Fact]
    public void Activity_SetsMidiActive_ThenClearsAfterFlashWindow()
    {
        var scheduler = new HistoricalScheduler();
        var status = new FakeMidiStatus { IsInputConnected = true, InputDeviceName = "Ableton Push" };
        var vm = new ShellStatusViewModel(status, new FakeOutputCatalog(), SettingsWith(null, "Push", null), scheduler);

        Assert.False(vm.MidiActive);

        status.Emit();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.MidiActive);

        scheduler.AdvanceBy(ShellStatusViewModel.FlashWindow + TimeSpan.FromMilliseconds(1));
        Assert.False(vm.MidiActive);
    }
}
