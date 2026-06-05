using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Beat Engine module (doc 12 Module 1 / the mock's "Beat Engine" panel): the live tempo readout
/// plus the tap/lock/half-double/nudge/set-downbeat/reset key grid. Every control emits a
/// <see cref="PerformanceAction"/> through the <see cref="IPerformanceActionDispatcher"/> (doc 04 — the
/// UI never calls the clock directly); the readout follows the shared <see cref="IBeatClock"/>.
/// "Auto" has no backend yet (no action kind) and is exposed as a disabled control.
/// </summary>
public sealed class BeatEngineViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IBeatClock? _beatClock;

    private string _sourceLabel = "—";
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

    public BeatEngineViewModel(IPerformanceActionDispatcher? dispatcher = null, IBeatClock? beatClock = null)
    {
        _dispatcher = dispatcher;
        _beatClock = beatClock;

        IObservable<bool> canEmit = Observable.Return(IsEnabled);
        TapCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatTapTempo), canEmit);
        LockToggleCommand = ReactiveCommand.Create(ToggleLock, canEmit);
        HalfCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatHalfTempo), canEmit);
        DoubleCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatDoubleTempo), canEmit);
        SetDownbeatCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatSetDownbeat), canEmit);
        NudgeForwardCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatNudgeForward), canEmit);
        NudgeBackwardCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatNudgeBackward), canEmit);
        ResetCommand = ReactiveCommand.Create(() => Emit(PerformanceActionKind.BeatResetGrid), canEmit);

        if (_beatClock is not null)
        {
            ApplyState(_beatClock.Current);
            _beatClock.StateChanged += OnBeatStateChanged;
        }
    }

    public ReactiveCommand<Unit, Unit> TapCommand { get; }
    public ReactiveCommand<Unit, Unit> LockToggleCommand { get; }
    public ReactiveCommand<Unit, Unit> HalfCommand { get; }
    public ReactiveCommand<Unit, Unit> DoubleCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDownbeatCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    /// <summary>True when intent can be emitted (the action layer is wired); the UI disables controls otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    /// <summary>Auto-tempo follow has no Core handler yet — surfaced disabled so the mock layout is complete.</summary>
    public bool IsAutoEnabled => false;

    /// <summary>The clock source feeding the readout (e.g. "Manual"), or "—" when idle.</summary>
    public string SourceLabel
    {
        get => _sourceLabel;
        private set => this.RaiseAndSetIfChanged(ref _sourceLabel, value);
    }

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

    /// <summary>True when the tempo is frozen (drives the Lock key's active state).</summary>
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_beatClock is not null)
            _beatClock.StateChanged -= OnBeatStateChanged;
    }

    // Lock is a single toggle key in the UI but two action kinds in the vocabulary, so emit the
    // opposite of the current latch (mirrors how a Push pad toggles lock).
    private void ToggleLock()
        => Emit(IsLocked ? PerformanceActionKind.BeatUnlock : PerformanceActionKind.BeatLock);

    private void Emit(PerformanceActionKind kind)
        => _dispatcher?.Dispatch(new PerformanceAction(kind));

    private void OnBeatStateChanged(object? sender, BeatClockState state)
        => RxApp.MainThreadScheduler.Schedule(() => ApplyState(state));

    private void ApplyState(BeatClockState state)
    {
        bool active = state.Bpm > 0;
        SourceLabel = active ? state.Source.ToString() : "—";
        Bpm = active ? $"{state.Bpm:0.0} BPM" : "—";
        Confidence = active ? $"{state.Confidence * 100:0}%" : "—";
        IsLocked = state.IsLocked;
        BeatPhase = state.BeatPhase;
        BarPhase = state.BarPhase;
        BeatCount = state.BeatCount;
        BarNumber = state.BarNumber;
        IsBeat = state.IsBeat;
        IsDownbeat = state.IsDownbeat;
    }
}
