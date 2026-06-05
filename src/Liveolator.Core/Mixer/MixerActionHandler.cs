using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Mixer;

/// <summary>
/// The dispatcher handler that owns the software-mixer actions (doc 04/11): crossfade, per-deck
/// gain, 3-band EQ, single-knob filter, and headphone-cue toggle. It holds the authoritative
/// <see cref="MixerState"/>, derives audible gains and biquad coefficients via <see cref="MixerMath"/>,
/// and pushes them to the realtime <see cref="IMixer"/> seam — so the UI, a controller, or autopilot
/// all drive the mixer through the one action layer. Pure managed; unit-tests with a fake mixer.
/// </summary>
public sealed class MixerActionHandler : PerformanceActionHandlerBase
{
    /// <summary>Default crossfade nudge per relative tick, before <see cref="PerformanceAction.Value"/> scaling.</summary>
    public const double DefaultRelativeStep = 1.0;

    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.MixerCrossfade,
        PerformanceActionKind.MixerChannelGain,
        PerformanceActionKind.MixerEqBand,
        PerformanceActionKind.MixerFilter,
        PerformanceActionKind.MixerCueToggle,
    };

    private readonly IMixer _mixer;
    private readonly int _sampleRate;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private MixerState _state = MixerState.Default;

    public MixerActionHandler(IMixer mixer, int sampleRate = 48_000, ILoggerFactory? loggerFactory = null)
    {
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _sampleRate = sampleRate;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MixerActionHandler>();
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <summary>The current authoritative mixer state (immutable snapshot semantics).</summary>
    public MixerState State
    {
        get { lock (_gate) return _state; }
    }

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case PerformanceActionKind.MixerCrossfade:
                ApplyCrossfade(action);
                break;
            case PerformanceActionKind.MixerChannelGain:
                ApplyChannelGain(action);
                break;
            case PerformanceActionKind.MixerEqBand:
                ApplyEqBand(action);
                break;
            case PerformanceActionKind.MixerFilter:
                ApplyFilter(action);
                break;
            case PerformanceActionKind.MixerCueToggle:
                ApplyCueToggle(action);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    private void ApplyCrossfade(PerformanceAction action)
    {
        lock (_gate)
        {
            double position = action.InputMode == ActionInputMode.Relative
                ? _state.Crossfader + (action.Value * DefaultRelativeStep)
                : action.Value;
            _state = _state.WithCrossfader(position);
            PushDeckGains();
        }
        RaiseCrossfadeFeedback();
        _logger.LogDebug("Crossfade set to {Position}", State.Crossfader);
    }

    private void ApplyChannelGain(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        lock (_gate)
        {
            double gain = ResolveAbsoluteOrDelta(action, _state.Channel(slot).Gain);
            _state = _state.WithChannel(slot, _state.Channel(slot) with { Gain = Math.Clamp(gain, 0.0, 1.0) });
            PushDeckGain(slot);
        }
    }

    private void ApplyEqBand(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        EqBand band = ParseBand(action.Argument);
        lock (_gate)
        {
            EqBands current = _state.Channel(slot).Eq;
            double value = ResolveAbsoluteOrDelta(action, BandValue(current, band));
            EqBands next = current.With(band, value);
            _state = _state.WithChannel(slot, _state.Channel(slot) with { Eq = next });
            _mixer.SetEqBand(slot, band, MixerMath.EqBandCoefficients(band, next, _sampleRate));
        }
    }

    private void ApplyFilter(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        lock (_gate)
        {
            double knob = ResolveAbsoluteOrDelta(action, _state.Channel(slot).Filter);
            knob = Math.Clamp(knob, 0.0, 1.0);
            _state = _state.WithChannel(slot, _state.Channel(slot) with { Filter = knob });
            _mixer.SetFilter(slot, MixerMath.FilterCoefficients(knob, _sampleRate));
        }
    }

    private void ApplyCueToggle(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        bool enabled;
        lock (_gate)
        {
            enabled = !_state.Channel(slot).CueEnabled;
            _state = _state.WithChannel(slot, _state.Channel(slot) with { CueEnabled = enabled });
            _mixer.SetCue(slot, enabled);
        }
        RaiseFeedback(
            PerformanceActionKind.MixerCueToggle, slot,
            new ActionFeedbackState(IsActive: enabled, IsAvailable: true, Value: 0));
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        MixerState state = State;
        return kind switch
        {
            PerformanceActionKind.MixerCrossfade
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Crossfader),
            PerformanceActionKind.MixerCueToggle when slot >= 0 && slot < state.Channels.Count
                => new ActionFeedbackState(IsActive: state.Channel(slot).CueEnabled, IsAvailable: true, Value: 0),
            PerformanceActionKind.MixerChannelGain when slot >= 0 && slot < state.Channels.Count
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Channel(slot).Gain),
            PerformanceActionKind.MixerFilter when slot >= 0 && slot < state.Channels.Count
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Channel(slot).Filter),
            _ => ActionFeedbackState.Unavailable,
        };
    }

    // Crossfader changes both decks' audible gain, so push both.
    private void PushDeckGains()
    {
        for (int slot = 0; slot < _state.Channels.Count; slot++)
            PushDeckGain(slot);
    }

    private void PushDeckGain(int slot) => _mixer.SetDeckGain(slot, MixerMath.DeckOutputGain(_state, slot));

    private void RaiseCrossfadeFeedback()
        => RaiseFeedback(
            PerformanceActionKind.MixerCrossfade, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: State.Crossfader));

    private static double ResolveAbsoluteOrDelta(PerformanceAction action, double current)
        => action.InputMode == ActionInputMode.Relative ? current + action.Value : action.Value;

    private static int ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= MixerState.DeckCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");
        return slot;
    }

    private static EqBand ParseBand(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || !Enum.TryParse(argument, ignoreCase: true, out EqBand band))
            throw new ArgumentException(
                "MixerEqBand requires Argument set to a band name (Low/Mid/High).", nameof(argument));
        return band;
    }

    private static double BandValue(EqBands eq, EqBand band) => band switch
    {
        EqBand.Low => eq.Low,
        EqBand.Mid => eq.Mid,
        _ => eq.High,
    };
}
