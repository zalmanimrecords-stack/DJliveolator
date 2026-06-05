using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using ReactiveUI;

namespace Liveolator.App.Features.Live;

/// <summary>
/// The Live performance tab. Drives a manual beat clock so tap-tempo, lock, nudge and the
/// beat/downbeat pulse are demonstrable with no audio hardware: every transport and beat control
/// emits a <see cref="PerformanceAction"/> through the <see cref="IPerformanceActionDispatcher"/>
/// (the doc 04 seam — the UI never calls an engine), and the live <see cref="BeatClockState"/> is
/// read from the <see cref="IBeatClock"/>. A render-loop timer (<see cref="ILiveBeatTimer"/>) ticks
/// the manual clock between taps so the grid advances smoothly.
/// Best-effort like the Libraries tab: when the dispatcher/clock are absent the controls disable and
/// the app still launches.
/// </summary>
public sealed class LiveViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IBeatClock? _beatClock;
    private readonly IManualBeatClockDriver? _clockDriver;
    private readonly IHostClock? _hostClock;
    private readonly ILiveBeatTimer? _timer;

    private string _bpm = "—";
    private string _confidence = "—";
    private bool _isLocked;
    private double _beatPhase;
    private double _barPhase;
    private int _beatCount;
    private int _barNumber;
    private bool _isBeat;
    private bool _isDownbeat;
    private bool _disposed;

    /// <param name="dispatcher">Action layer for transport/beat intent; null disables the controls.</param>
    /// <param name="beatClock">Live clock to display; null leaves the readout idle.</param>
    /// <param name="clockDriver">The manual clock's render-loop pump; null disables smooth advance.</param>
    /// <param name="hostClock">Monotonic host time used to stamp each pump tick.</param>
    /// <param name="timer">Render-loop seam driving <paramref name="clockDriver"/>; null disables it.</param>
    public LiveViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IBeatClock? beatClock = null,
        IManualBeatClockDriver? clockDriver = null,
        IHostClock? hostClock = null,
        ILiveBeatTimer? timer = null)
    {
        _dispatcher = dispatcher;
        _beatClock = beatClock;
        _clockDriver = clockDriver;
        _hostClock = hostClock;
        _timer = timer;

        TapCommand = MakeBeatCommand(PerformanceActionKind.BeatTapTempo);
        LockCommand = MakeBeatCommand(PerformanceActionKind.BeatLock);
        UnlockCommand = MakeBeatCommand(PerformanceActionKind.BeatUnlock);
        HalfCommand = MakeBeatCommand(PerformanceActionKind.BeatHalfTempo);
        DoubleCommand = MakeBeatCommand(PerformanceActionKind.BeatDoubleTempo);
        NudgeForwardCommand = MakeBeatCommand(PerformanceActionKind.BeatNudgeForward);
        NudgeBackwardCommand = MakeBeatCommand(PerformanceActionKind.BeatNudgeBackward);
        SetDownbeatCommand = MakeBeatCommand(PerformanceActionKind.BeatSetDownbeat);

        PlayPauseCommand = MakeBeatCommand(PerformanceActionKind.DeckPlayPause);
        StopCommand = MakeBeatCommand(PerformanceActionKind.TransportStop);

        if (_beatClock is not null)
        {
            ApplyState(_beatClock.Current);
            _beatClock.StateChanged += OnBeatStateChanged;
        }

        if (_timer is not null && _clockDriver is not null && _hostClock is not null)
        {
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    public ReactiveCommand<Unit, Unit> TapCommand { get; }
    public ReactiveCommand<Unit, Unit> LockCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlockCommand { get; }
    public ReactiveCommand<Unit, Unit> HalfCommand { get; }
    public ReactiveCommand<Unit, Unit> DoubleCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDownbeatCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>True when intent can be emitted (the action layer is wired). The UI disables controls otherwise.</summary>
    public bool IsLiveModeEnabled => _dispatcher is not null;

    /// <summary>Current tempo, e.g. "128.0 BPM", or "—" before the first tap establishes one.</summary>
    public string Bpm
    {
        get => _bpm;
        private set => this.RaiseAndSetIfChanged(ref _bpm, value);
    }

    /// <summary>Detection confidence as a percentage, or "—" when idle.</summary>
    public string Confidence
    {
        get => _confidence;
        private set => this.RaiseAndSetIfChanged(ref _confidence, value);
    }

    /// <summary>True when the tempo is frozen (drives the lock indicator/LED).</summary>
    public bool IsLocked
    {
        get => _isLocked;
        private set => this.RaiseAndSetIfChanged(ref _isLocked, value);
    }

    /// <summary>Position within the current beat, 0..1 (drives the phase bar).</summary>
    public double BeatPhase
    {
        get => _beatPhase;
        private set => this.RaiseAndSetIfChanged(ref _beatPhase, value);
    }

    /// <summary>Position within the current bar, 0..1.</summary>
    public double BarPhase
    {
        get => _barPhase;
        private set => this.RaiseAndSetIfChanged(ref _barPhase, value);
    }

    /// <summary>Monotonic beat count since the last grid reset.</summary>
    public int BeatCount
    {
        get => _beatCount;
        private set => this.RaiseAndSetIfChanged(ref _beatCount, value);
    }

    /// <summary>Current bar number.</summary>
    public int BarNumber
    {
        get => _barNumber;
        private set => this.RaiseAndSetIfChanged(ref _barNumber, value);
    }

    /// <summary>True on the frame a beat boundary is crossed — pulses the beat indicator.</summary>
    public bool IsBeat
    {
        get => _isBeat;
        private set => this.RaiseAndSetIfChanged(ref _isBeat, value);
    }

    /// <summary>True on the frame a bar boundary is crossed — pulses the downbeat indicator.</summary>
    public bool IsDownbeat
    {
        get => _isDownbeat;
        private set => this.RaiseAndSetIfChanged(ref _isDownbeat, value);
    }

    /// <summary>Stops the render loop and detaches the clock — call when the tab/window closes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_timer is not null)
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
        }

        if (_beatClock is not null)
            _beatClock.StateChanged -= OnBeatStateChanged;
    }

    private ReactiveCommand<Unit, Unit> MakeBeatCommand(PerformanceActionKind kind)
        => ReactiveCommand.Create(() => Emit(kind), this.WhenAnyValue(x => x.IsLiveModeEnabled));

    private void Emit(PerformanceActionKind kind)
        => _dispatcher?.Dispatch(new PerformanceAction(kind));

    // The render loop pumps the manual clock so phase + the pulse advance smoothly between taps.
    private void OnTimerTick(object? sender, EventArgs e)
        => _clockDriver?.Update(_hostClock!.NowTicks);

    private void OnBeatStateChanged(object? sender, BeatClockState state)
        => RxApp.MainThreadScheduler.Schedule(() => ApplyState(state));

    private void ApplyState(BeatClockState state)
    {
        Bpm = state.Bpm > 0 ? $"{state.Bpm:0.0} BPM" : "—";
        Confidence = state.Bpm > 0 ? $"{state.Confidence * 100:0}%" : "—";
        IsLocked = state.IsLocked;
        BeatPhase = state.BeatPhase;
        BarPhase = state.BarPhase;
        BeatCount = state.BeatCount;
        BarNumber = state.BarNumber;
        IsBeat = state.IsBeat;
        IsDownbeat = state.IsDownbeat;
    }
}
