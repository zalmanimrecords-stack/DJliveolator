using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using ReactiveUI;

namespace Liveolator.App.Shell;

/// <summary>
/// Top-bar status: where audio is routed and which MIDI controller is connected, plus a green
/// "signal" flash on each inbound MIDI message. Presentation only — it reads the resolved audio
/// device name from the output catalog and binds the live connection/activity off
/// <see cref="IMidiControlStatus"/>. UI-free and unit-testable (the flash uses an injected scheduler).
/// </summary>
public sealed class ShellStatusViewModel : ViewModelBase, IDisposable
{
    /// <summary>Sentinel shown when no specific output device is selected (or it is gone).</summary>
    public const string SystemDefault = "System default";

    /// <summary>How long the MIDI indicator stays green after the last message before settling.</summary>
    public static readonly TimeSpan FlashWindow = TimeSpan.FromMilliseconds(160);

    /// <summary>Default cadence for the CPU/memory readout (slow enough to be unobtrusive).</summary>
    public static readonly TimeSpan DefaultMetricsInterval = TimeSpan.FromMilliseconds(1500);

    private readonly IDisposable _activity;
    private bool _midiActive;
    private string _cpuText = "—";
    private string _memoryText = "—";

    /// <param name="metrics">Live CPU/memory sampler; null hides the readout (unit tests omit it).</param>
    /// <param name="metricsInterval">Poll cadence; defaults to <see cref="DefaultMetricsInterval"/>.</param>
    public ShellStatusViewModel(
        IMidiControlStatus midi,
        IAudioOutputDeviceCatalog outputs,
        AppSettings settings,
        IScheduler? scheduler = null,
        ISystemMetricsSampler? metrics = null,
        TimeSpan? metricsInterval = null)
    {
        ArgumentNullException.ThrowIfNull(midi);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(settings);

        IScheduler sched = scheduler ?? RxApp.MainThreadScheduler;

        AudioOutputName = ResolveOutputName(outputs, settings.Audio.OutputDeviceId);

        MidiInputConnected = midi.IsInputConnected;
        MidiInputName = midi.InputDeviceName ?? settings.Midi.ControllerInputName ?? "No controller";
        MidiFeedbackName = midi.OutputDeviceName ?? settings.Midi.FeedbackOutputName;

        // Each message turns the indicator green; the green clears after FlashWindow of silence.
        // ObserveOn marshals off the MIDI callback thread onto the UI (or test) scheduler.
        IObservable<EventArgs> activity = Observable
            .FromEventPattern<EventHandler, EventArgs>(h => midi.ActivityDetected += h, h => midi.ActivityDetected -= h)
            .Select(e => e.EventArgs)
            .ObserveOn(sched);

        var subscriptions = new CompositeDisposable(
            activity.Subscribe(_ => MidiActive = true),
            activity.Throttle(FlashWindow, sched).Subscribe(_ => MidiActive = false));

        // Live CPU / memory readout — only when a sampler is supplied (keeps the unit tests, which omit it,
        // free of a polling timer). Seed immediately so the bar shows a value on open, then poll on the
        // shared scheduler.
        if (metrics is not null)
        {
            HasMetrics = true;
            ApplyMetrics(metrics.Sample());
            subscriptions.Add(Observable
                .Interval(metricsInterval ?? DefaultMetricsInterval, sched)
                .Subscribe(_ => ApplyMetrics(metrics.Sample())));
        }

        _activity = subscriptions;
    }

    /// <summary>The output device audio is routed to (or <see cref="SystemDefault"/>).</summary>
    public string AudioOutputName { get; }

    /// <summary>The connected (or configured) controller device name.</summary>
    public string MidiInputName { get; }

    /// <summary>True once the controller input is open.</summary>
    public bool MidiInputConnected { get; }

    /// <summary>The feedback (LED) device name, or null when none is configured/connected.</summary>
    public string? MidiFeedbackName { get; }

    /// <summary>Pulses true on each inbound MIDI message; drives the green signal LED.</summary>
    public bool MidiActive
    {
        get => _midiActive;
        private set => this.RaiseAndSetIfChanged(ref _midiActive, value);
    }

    /// <summary>True when a metrics sampler is wired (drives the CPU/RAM readout's visibility).</summary>
    public bool HasMetrics { get; }

    /// <summary>Current CPU load as a short label, e.g. "34%".</summary>
    public string CpuText
    {
        get => _cpuText;
        private set => this.RaiseAndSetIfChanged(ref _cpuText, value);
    }

    /// <summary>Current resident memory as a short label, e.g. "412 MB".</summary>
    public string MemoryText
    {
        get => _memoryText;
        private set => this.RaiseAndSetIfChanged(ref _memoryText, value);
    }

    private void ApplyMetrics(SystemMetrics m)
    {
        CpuText = $"{m.CpuPercent:0}%";
        MemoryText = $"{m.MemoryMb:0} MB";
    }

    private static string ResolveOutputName(IAudioOutputDeviceCatalog outputs, string? outputDeviceId)
    {
        if (string.IsNullOrEmpty(outputDeviceId))
            return SystemDefault;

        return outputs.EnumerateOutputDevices().FirstOrDefault(d => d.Id == outputDeviceId)?.Name
            ?? SystemDefault;
    }

    public void Dispose() => _activity.Dispose();
}
