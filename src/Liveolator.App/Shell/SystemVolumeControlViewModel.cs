using System.Reactive.Concurrency;
using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Shell;

/// <summary>
/// The shell's global volume knob: a single control that drives the COMPUTER's OS master volume through
/// the dispatcher (<see cref="PerformanceActionKind.SystemMasterVolume"/>), exactly like any other action
/// source (doc 04). It seeds from the current system volume and follows feedback (e.g. a MIDI controller
/// moving the same level). When the host cannot control the OS volume the knob disables itself
/// (<see cref="IsAvailable"/> = false) rather than emitting no-ops.
/// </summary>
public sealed class SystemVolumeControlViewModel : ViewModelBase, IDisposable
{
    // Fallback shown only when the live system level is unreadable (it never is when IsAvailable is true).
    private const double DefaultLevel = 0.75;

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private bool _disposed;

    public SystemVolumeControlViewModel(IPerformanceActionDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;

        ActionFeedbackState? seed = dispatcher?.GetFeedback(PerformanceActionKind.SystemMasterVolume, 0);
        IsAvailable = seed is { IsAvailable: true };
        double initial = IsAvailable ? seed!.Value : DefaultLevel;

        Volume = new ContinuousControlViewModel(
            "VOL", initial,
            IsAvailable ? Emit : null);

        if (_dispatcher is not null && IsAvailable)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>The 0..1 system-volume control, two-way bound to the shell knob.</summary>
    public ContinuousControlViewModel Volume { get; }

    /// <summary>True when this host exposes a controllable OS master volume; the knob is hidden otherwise.</summary>
    public bool IsAvailable { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null && IsAvailable)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private void Emit(double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute, Value: value));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind != PerformanceActionKind.SystemMasterVolume)
            return;
        RxApp.MainThreadScheduler.Schedule(() => Volume.SetFromFeedback(e.State.Value));
    }
}
