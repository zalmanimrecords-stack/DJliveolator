using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Master / FX module (the mock's Master·FX panel): the Strobe and Blackout toggles plus the master
/// REC toggle, driven through the dispatcher to the visual and recording handlers (doc 04/08, roadmap X2).
/// Each on/off latch is read back from dispatcher feedback so the buttons reflect the engine's true state.
/// The Master-gain and Swing knobs have no action kind yet (doc 18) and are surfaced as disabled controls.
/// </summary>
public sealed class MasterFxViewModel : ViewModelBase, IDisposable
{
    private const double DefaultMaster = 0.7;
    private const double DefaultSwing = 0.0;

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private bool _isStrobe;
    private bool _isBlackout;
    private bool _isRecording;
    private bool _isRecordEnabled;
    private bool _disposed;

    public MasterFxViewModel(IPerformanceActionDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
        IObservable<bool> canEmit = Observable.Return(dispatcher is not null);

        StrobeCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.VisualToggleStrobe), canEmit);
        BlackoutCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.VisualBlackout), canEmit);
        RecordCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.MasterRecordToggle), canEmit);

        // No MixerMasterGain / swing action kind yet — disabled (null callback).
        Master = new ContinuousControlViewModel("Master", DefaultMaster, onUserChanged: null);
        Swing = new ContinuousControlViewModel("Swing", DefaultSwing, onUserChanged: null);

        if (_dispatcher is not null)
        {
            _isStrobe = _dispatcher.GetFeedback(PerformanceActionKind.VisualToggleStrobe).IsActive;
            _isBlackout = _dispatcher.GetFeedback(PerformanceActionKind.VisualBlackout).IsActive;
            ActionFeedbackState record = _dispatcher.GetFeedback(PerformanceActionKind.MasterRecordToggle);
            _isRecording = record.IsActive;
            _isRecordEnabled = record.IsAvailable;
            _dispatcher.FeedbackChanged += OnFeedback;
        }
    }

    /// <summary>True when the visual handler is wired; the UI disables the FX buttons otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    public ReactiveCommand<Unit, Unit> StrobeCommand { get; }
    public ReactiveCommand<Unit, Unit> BlackoutCommand { get; }
    public ReactiveCommand<Unit, Unit> RecordCommand { get; }
    public ContinuousControlViewModel Master { get; }
    public ContinuousControlViewModel Swing { get; }

    /// <summary>True while the strobe overlay is engaged (drives the Strobe button's active state).</summary>
    public bool IsStrobe
    {
        get => _isStrobe;
        private set => this.RaiseAndSetIfChanged(ref _isStrobe, value);
    }

    /// <summary>True while output is blacked out (drives the Blackout button's active state).</summary>
    public bool IsBlackout
    {
        get => _isBlackout;
        private set => this.RaiseAndSetIfChanged(ref _isBlackout, value);
    }

    /// <summary>True while the master mix is being recorded (drives the REC button's active state).</summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    /// <summary>True when a recordable master exists (realtime audio is up); the REC button greys out otherwise.</summary>
    public bool IsRecordEnabled
    {
        get => _isRecordEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isRecordEnabled, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private void Emit(PerformanceActionKind kind) => _dispatcher?.Dispatch(new PerformanceAction(kind));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
        => RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.VisualToggleStrobe:
                    IsStrobe = e.State.IsActive;
                    break;
                case PerformanceActionKind.VisualBlackout:
                    IsBlackout = e.State.IsActive;
                    break;
                case PerformanceActionKind.MasterRecordToggle:
                    IsRecording = e.State.IsActive;
                    IsRecordEnabled = e.State.IsAvailable;
                    break;
            }
        });
}
