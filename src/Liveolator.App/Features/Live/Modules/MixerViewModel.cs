using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Mixer;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Mixer module (the mock's centre column / doc 11): the A↔B crossfader and the two per-deck
/// channel-gain faders. Each fader drives the <see cref="MixerActionHandler"/> through the dispatcher
/// (doc 04). Per-deck peak meters read post-processing samples through <see cref="IDeckLevelMeter"/>.
/// Initial fader positions are seeded from dispatcher feedback so the UI reflects the authoritative
/// mixer state.
/// </summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private const double DefaultCrossfader = 0.5;
    private const double DefaultGain = 1.0;
    private const double DefaultCueLevel = 1.0;
    private const double DefaultCueMix = 0.0; // 0 = full cue (PFL), 1 = full master

    private const double DefaultAutoMixTime = 0.6; // detent index 3 of 5 → 16 bars

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IDeckLevelMeter? _levelMeter;
    private bool _isCueA;
    private bool _isCueB;
    private double _levelA;
    private double _levelB;
    private bool _isAutoMixActive;
    private string _autoMixBars = "16";
    private string _autoMixStyle = "CrossFade";
    private bool _disposed;

    public MixerViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IDeckLevelMeter? levelMeter = null)
    {
        _dispatcher = dispatcher;
        _levelMeter = levelMeter;
        bool enabled = dispatcher is not null;

        Crossfader = new ContinuousControlViewModel(
            "A / B", Seed(PerformanceActionKind.MixerCrossfade, slot: 0, DefaultCrossfader),
            enabled ? v => Emit(PerformanceActionKind.MixerCrossfade, v, slot: 0) : null);

        ChannelGainA = new ContinuousControlViewModel(
            "A", Seed(PerformanceActionKind.MixerChannelGain, slot: 0, DefaultGain),
            enabled ? v => Emit(PerformanceActionKind.MixerChannelGain, v, slot: 0) : null);

        ChannelGainB = new ContinuousControlViewModel(
            "B", Seed(PerformanceActionKind.MixerChannelGain, slot: 1, DefaultGain),
            enabled ? v => Emit(PerformanceActionKind.MixerChannelGain, v, slot: 1) : null);

        IObservable<bool> canCue = Observable.Return(enabled);
        CueACommand = ReactiveCommand.Create(() => EmitCue(slot: 0), canCue);
        CueBCommand = ReactiveCommand.Create(() => EmitCue(slot: 1), canCue);

        CueLevel = new ContinuousControlViewModel(
            "Cue", Seed(PerformanceActionKind.MixerCueLevel, slot: 0, DefaultCueLevel),
            enabled ? v => Emit(PerformanceActionKind.MixerCueLevel, v, slot: 0) : null);

        CueMix = new ContinuousControlViewModel(
            "Cue / Master", Seed(PerformanceActionKind.MixerCueMix, slot: 0, DefaultCueMix),
            enabled ? v => Emit(PerformanceActionKind.MixerCueMix, v, slot: 0) : null);

        // AUTOMIX (doc 11): available only when its handler is wired (realtime engine up) — the
        // button stays disabled in headless/catalog mode rather than silently dropping the action.
        IsAutoMixAvailable =
            dispatcher?.GetFeedback(PerformanceActionKind.AutomixToggle, 0).IsAvailable ?? false;
        IObservable<bool> canAutoMix = Observable.Return(IsAutoMixAvailable);
        AutoMixCommand = ReactiveCommand.Create(
            () => { _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.AutomixToggle)); },
            canAutoMix);
        AutoMixStyleCommand = ReactiveCommand.Create<string>(
            style => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.AutomixSetStyle, Argument: style)),
            canAutoMix);
        AutoMixTime = new ContinuousControlViewModel(
            "Time", Seed(PerformanceActionKind.AutomixSetDuration, slot: 0, DefaultAutoMixTime),
            IsAutoMixAvailable ? v => Emit(PerformanceActionKind.AutomixSetDuration, v, slot: 0) : null);

        if (_dispatcher is not null)
        {
            _isCueA = _dispatcher.GetFeedback(PerformanceActionKind.MixerCueToggle, 0).IsActive;
            _isCueB = _dispatcher.GetFeedback(PerformanceActionKind.MixerCueToggle, 1).IsActive;
            if (IsAutoMixAvailable)
            {
                ActionFeedbackState toggle = _dispatcher.GetFeedback(PerformanceActionKind.AutomixToggle, 0);
                _isAutoMixActive = toggle.IsActive;
                _autoMixBars =
                    _dispatcher.GetFeedback(PerformanceActionKind.AutomixSetDuration, 0).Argument ?? "16";
                _autoMixStyle =
                    _dispatcher.GetFeedback(PerformanceActionKind.AutomixSetStyle, 0).Argument ?? "CrossFade";
            }
            _dispatcher.FeedbackChanged += OnFeedback;
        }
    }

    /// <summary>True when the mixer handler is wired; the UI disables the faders otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    public ContinuousControlViewModel Crossfader { get; }
    public ContinuousControlViewModel ChannelGainA { get; }
    public ContinuousControlViewModel ChannelGainB { get; }

    public double LevelA
    {
        get => _levelA;
        private set => this.RaiseAndSetIfChanged(ref _levelA, value);
    }

    public double LevelB
    {
        get => _levelB;
        private set => this.RaiseAndSetIfChanged(ref _levelB, value);
    }

    public void UpdateLevels(bool deckAPlaying, bool deckBPlaying)
    {
        if (_levelMeter is null)
            return;
        LevelA = deckAPlaying ? _levelMeter.GetLevel(0).Peak : 0;
        LevelB = deckBPlaying ? _levelMeter.GetLevel(1).Peak : 0;
    }

    /// <summary>Headphone-cue (PFL) bus output level (MixerCueLevel).</summary>
    public ContinuousControlViewModel CueLevel { get; }

    /// <summary>Cue/master blend knob: 0 = pre-listen the cued decks, 1 = the master (MixerCueMix).</summary>
    public ContinuousControlViewModel CueMix { get; }

    /// <summary>Headphone-cue toggles per deck (MixerCueToggle — a ready handler).</summary>
    public ReactiveCommand<Unit, Unit> CueACommand { get; }
    public ReactiveCommand<Unit, Unit> CueBCommand { get; }

    /// <summary>True when the auto-mix handler is wired (realtime engine up).</summary>
    public bool IsAutoMixAvailable { get; }

    /// <summary>Engage/abort the hands-free transition (AutomixToggle).</summary>
    public ReactiveCommand<Unit, Unit> AutoMixCommand { get; }

    /// <summary>Select the transition style; parameter = CrossFade / EqMix / FxMix (AutomixSetStyle).</summary>
    public ReactiveCommand<string, Unit> AutoMixStyleCommand { get; }

    /// <summary>The transition-length TIME knob (AutomixSetDuration; detents 2..64 bars).</summary>
    public ContinuousControlViewModel AutoMixTime { get; }

    /// <summary>True while a transition is armed/syncing/running (button LED, from feedback).</summary>
    public bool IsAutoMixActive
    {
        get => _isAutoMixActive;
        private set => this.RaiseAndSetIfChanged(ref _isAutoMixActive, value);
    }

    /// <summary>The resolved transition length in bars, as reported by the engine.</summary>
    public string AutoMixBars
    {
        get => _autoMixBars;
        private set
        {
            this.RaiseAndSetIfChanged(ref _autoMixBars, value);
            this.RaisePropertyChanged(nameof(AutoMixBarsLabel));
        }
    }

    /// <summary>Knob caption, e.g. "16 BARS".</summary>
    public string AutoMixBarsLabel => $"{_autoMixBars} BARS";

    /// <summary>The selected style name (CrossFade/EqMix/FxMix), for lighting the style keys.</summary>
    public string AutoMixStyle
    {
        get => _autoMixStyle;
        private set
        {
            this.RaiseAndSetIfChanged(ref _autoMixStyle, value);
            this.RaisePropertyChanged(nameof(IsStyleCrossFade));
            this.RaisePropertyChanged(nameof(IsStyleEqMix));
            this.RaisePropertyChanged(nameof(IsStyleFxMix));
        }
    }

    public bool IsStyleCrossFade => _autoMixStyle == "CrossFade";
    public bool IsStyleEqMix => _autoMixStyle == "EqMix";
    public bool IsStyleFxMix => _autoMixStyle == "FxMix";

    /// <summary>True while deck A/B is routed to the headphone cue bus (from dispatcher feedback).</summary>
    public bool IsCueA
    {
        get => _isCueA;
        private set => this.RaiseAndSetIfChanged(ref _isCueA, value);
    }

    public bool IsCueB
    {
        get => _isCueB;
        private set => this.RaiseAndSetIfChanged(ref _isCueB, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private double Seed(PerformanceActionKind kind, int slot, double fallback)
    {
        ActionFeedbackState? feedback = _dispatcher?.GetFeedback(kind, slot);
        return feedback is { IsAvailable: true } ? feedback.Value : fallback;
    }

    private void Emit(PerformanceActionKind kind, double value, int slot)
        => _dispatcher?.Dispatch(new PerformanceAction(kind, ActionInputMode.Absolute, Value: value, Slot: slot));

    private void EmitCue(int slot)
        => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.MixerCueToggle, Slot: slot));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
        => RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.MixerCrossfade:
                    Crossfader.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerChannelGain when e.Slot == 0:
                    ChannelGainA.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerChannelGain when e.Slot == 1:
                    ChannelGainB.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerCueToggle when e.Slot == 0:
                    IsCueA = e.State.IsActive;
                    break;
                case PerformanceActionKind.MixerCueToggle when e.Slot == 1:
                    IsCueB = e.State.IsActive;
                    break;
                case PerformanceActionKind.MixerCueLevel:
                    CueLevel.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerCueMix:
                    CueMix.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.AutomixToggle:
                    IsAutoMixActive = e.State.IsActive;
                    break;
                case PerformanceActionKind.AutomixSetDuration:
                    AutoMixTime.SetFromFeedback(e.State.Value);
                    if (e.State.Argument is { } bars)
                        AutoMixBars = bars;
                    break;
                case PerformanceActionKind.AutomixSetStyle:
                    if (e.State.Argument is { } style)
                        AutoMixStyle = style;
                    break;
            }
        });
}
