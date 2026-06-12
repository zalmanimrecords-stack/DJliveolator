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

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IDeckLevelMeter? _levelMeter;
    private bool _isCueA;
    private bool _isCueB;
    private double _levelA;
    private double _levelB;
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

        if (_dispatcher is not null)
        {
            _isCueA = _dispatcher.GetFeedback(PerformanceActionKind.MixerCueToggle, 0).IsActive;
            _isCueB = _dispatcher.GetFeedback(PerformanceActionKind.MixerCueToggle, 1).IsActive;
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
            }
        });
}
