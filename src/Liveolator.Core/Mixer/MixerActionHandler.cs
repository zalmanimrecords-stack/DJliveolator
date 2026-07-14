using Liveolator.Core.Actions;
using Liveolator.Core.Dsp;
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
        PerformanceActionKind.MixerEqKill,
        PerformanceActionKind.MixerFilter,
        PerformanceActionKind.MixerCueToggle,
        PerformanceActionKind.MixerCueLevel,
        PerformanceActionKind.MixerCueMix,
        PerformanceActionKind.MixerEqCutMode,
        PerformanceActionKind.MixerLimiterSmart,
        PerformanceActionKind.MixerLimiterCharacter,
        PerformanceActionKind.MixerLimiterCeiling,
    };

    /// <summary>UI/controller-meaningful true-peak ceiling range (dBTP): hot but never full scale.</summary>
    private const double CeilingMaxDbTp = -0.3;
    private const double CeilingMinDbTp = -2.0;

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
            case PerformanceActionKind.MixerEqKill:
                ApplyEqKill(action);
                break;
            case PerformanceActionKind.MixerFilter:
                ApplyFilter(action);
                break;
            case PerformanceActionKind.MixerCueToggle:
                ApplyCueToggle(action);
                break;
            case PerformanceActionKind.MixerCueLevel:
                ApplyCueLevel(action);
                break;
            case PerformanceActionKind.MixerCueMix:
                ApplyCueMix(action);
                break;
            case PerformanceActionKind.MixerEqCutMode:
                ApplyEqCutMode(action);
                break;
            case PerformanceActionKind.MixerLimiterSmart:
                ApplyLimiterSmart();
                break;
            case PerformanceActionKind.MixerLimiterCharacter:
                ApplyLimiterCharacter(action);
                break;
            case PerformanceActionKind.MixerLimiterCeiling:
                ApplyLimiterCeiling(action);
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
        RaiseFeedback(
            PerformanceActionKind.MixerChannelGain, slot,
            ValueFeedback(State.Channel(slot).Gain));
    }

    // Per-(deck, band) value saved when an EQ-kill press cuts the band, so the release can restore it.
    private readonly Dictionary<(int Slot, EqBand Band), double> _eqKillSaved = new();

    private void ApplyEqBand(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        EqBand band = ParseBand(action.Argument);
        lock (_gate)
        {
            double value = ResolveAbsoluteOrDelta(action, BandValue(_state.Channel(slot).Eq, band));
            SetBandLocked(slot, band, value);
        }
        RaiseFeedback(
            PerformanceActionKind.MixerEqBand, slot,
            ValueFeedback(BandValue(State.Channel(slot).Eq, band), band.ToString()));
    }

    // Momentary EQ kill (doc 31): press fully cuts the band (remembering where it was); release restores
    // it. A second press before a release is idempotent (the kept value stays the pre-kill one).
    private void ApplyEqKill(PerformanceAction action)
    {
        int slot = ValidateSlot(action.Slot);
        EqBand band = ParseBand(action.Argument);
        lock (_gate)
        {
            if (action.IsPressed)
            {
                _eqKillSaved.TryAdd((slot, band), BandValue(_state.Channel(slot).Eq, band));
                SetBandLocked(slot, band, 0.0); // 0 = full cut
            }
            else if (_eqKillSaved.Remove((slot, band), out double saved))
            {
                SetBandLocked(slot, band, saved);
            }
        }
        RaiseFeedback(
            PerformanceActionKind.MixerEqBand, slot,
            ValueFeedback(BandValue(State.Channel(slot).Eq, band), band.ToString()));
    }

    // Caller holds _gate. Set one band's normalized value into the state and push the coefficients.
    private void SetBandLocked(int slot, EqBand band, double value)
    {
        EqBands next = _state.Channel(slot).Eq.With(band, value);
        _state = _state.WithChannel(slot, _state.Channel(slot) with { Eq = next });
        _mixer.SetEqBand(slot, band, MixerMath.EqBandCoefficients(band, next, _sampleRate, _state.CutMode));
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
        RaiseFeedback(
            PerformanceActionKind.MixerFilter, slot,
            ValueFeedback(State.Channel(slot).Filter));
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

    private void ApplyCueLevel(PerformanceAction action)
    {
        double level;
        lock (_gate)
        {
            level = ResolveAbsoluteOrDelta(action, _state.CueBus.Level);
            _state = _state.WithCueBus(_state.CueBus.WithLevel(level));
            PushCueOutputGains();
        }
        RaiseFeedback(
            PerformanceActionKind.MixerCueLevel, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: State.CueBus.Level));
        _logger.LogDebug("Cue level set to {Level}", State.CueBus.Level);
    }

    private void ApplyCueMix(PerformanceAction action)
    {
        double mix;
        lock (_gate)
        {
            mix = ResolveAbsoluteOrDelta(action, _state.CueBus.Mix);
            _state = _state.WithCueBus(_state.CueBus.WithMix(mix));
            PushCueOutputGains();
        }
        RaiseFeedback(
            PerformanceActionKind.MixerCueMix, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: State.CueBus.Mix));
        _logger.LogDebug("Cue mix set to {Mix}", State.CueBus.Mix);
    }

    // The EQ cut-depth mode is mixer-wide, so changing it rebuilds and re-pushes every channel's EQ
    // band coefficients (their cut floor moved). Absolute select when Argument names a mode; otherwise
    // cycle to the next, progressively coarser mode — the single global button's behaviour.
    private void ApplyEqCutMode(PerformanceAction action)
    {
        EqCutMode mode;
        lock (_gate)
        {
            mode = ParseCutMode(action.Argument) ?? _state.CutMode.Next();
            _state = _state.WithCutMode(mode);
            for (int slot = 0; slot < _state.Channels.Count; slot++)
            {
                EqBands eq = _state.Channel(slot).Eq;
                _mixer.SetEqBand(slot, EqBand.Low, MixerMath.EqBandCoefficients(EqBand.Low, eq, _sampleRate, mode));
                _mixer.SetEqBand(slot, EqBand.Mid, MixerMath.EqBandCoefficients(EqBand.Mid, eq, _sampleRate, mode));
                _mixer.SetEqBand(slot, EqBand.High, MixerMath.EqBandCoefficients(EqBand.High, eq, _sampleRate, mode));
            }
        }
        RaiseFeedback(PerformanceActionKind.MixerEqCutMode, slot: 0, CutModeFeedback(mode));
        _logger.LogDebug("EQ cut mode set to {Mode}", mode);
    }

    // SAFE↔SMART toggle: flip the program-dependent-release mode and push the whole limiter settings to
    // the realtime master limiter. Character/ceiling ride along unchanged.
    private void ApplyLimiterSmart()
    {
        bool smart;
        lock (_gate)
        {
            smart = !_state.Limiter.SmartRelease;
            _state = _state.WithLimiter(_state.Limiter with { SmartRelease = smart });
            PushLimiter();
        }
        RaiseFeedback(PerformanceActionKind.MixerLimiterSmart, slot: 0,
            new ActionFeedbackState(IsActive: smart, IsAvailable: true, Value: 0));
        _logger.LogDebug("Smart limiter {Mode}", smart ? "SMART" : "SAFE");
    }

    private void ApplyLimiterCharacter(PerformanceAction action)
    {
        lock (_gate)
        {
            double character = Math.Clamp(ResolveAbsoluteOrDelta(action, _state.Limiter.Character), 0.0, 1.0);
            _state = _state.WithLimiter(_state.Limiter with { Character = character });
            PushLimiter();
        }
        RaiseFeedback(PerformanceActionKind.MixerLimiterCharacter, slot: 0,
            ValueFeedback(State.Limiter.Character));
    }

    private void ApplyLimiterCeiling(PerformanceAction action)
    {
        lock (_gate)
        {
            double ceiling = Math.Clamp(
                ResolveAbsoluteOrDelta(action, _state.Limiter.CeilingDbTp), CeilingMinDbTp, CeilingMaxDbTp);
            _state = _state.WithLimiter(_state.Limiter with { CeilingDbTp = ceiling });
            PushLimiter();
        }
        RaiseFeedback(PerformanceActionKind.MixerLimiterCeiling, slot: 0,
            ValueFeedback(State.Limiter.CeilingDbTp));
    }

    private void PushLimiter() => _mixer.SetLimiter(_state.Limiter);

    // The cue output mix (cued decks vs master, scaled by headphone level) depends only on the cue
    // bus controls, so push it whenever level or mix changes; per-deck PFL routing rides SetCue.
    private void PushCueOutputGains()
    {
        (double cueGain, double masterGain) = CueMixMath.HeadphoneOutputGains(_state.CueBus);
        _mixer.SetCueOutputGains(cueGain, masterGain);
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
            PerformanceActionKind.MixerCueLevel
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.CueBus.Level),
            PerformanceActionKind.MixerCueMix
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.CueBus.Mix),
            PerformanceActionKind.MixerChannelGain when slot >= 0 && slot < state.Channels.Count
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Channel(slot).Gain),
            PerformanceActionKind.MixerFilter when slot >= 0 && slot < state.Channels.Count
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Channel(slot).Filter),
            PerformanceActionKind.MixerEqCutMode
                => CutModeFeedback(state.CutMode),
            PerformanceActionKind.MixerLimiterSmart
                => new ActionFeedbackState(IsActive: state.Limiter.SmartRelease, IsAvailable: true, Value: 0),
            PerformanceActionKind.MixerLimiterCharacter
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Limiter.Character),
            PerformanceActionKind.MixerLimiterCeiling
                => new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: state.Limiter.CeilingDbTp),
            _ => ActionFeedbackState.Unavailable,
        };
    }

    // Reports the active cut mode as both its enum index (Value) and its name (Argument) so a button
    // face can show the label without the UI needing to know the dB ladder.
    private static ActionFeedbackState CutModeFeedback(EqCutMode mode)
        => new(IsActive: mode != EqCutMode.Kill, IsAvailable: true, Value: (int)mode, Argument: mode.ToString());

    private static EqCutMode? ParseCutMode(string? argument)
        => string.IsNullOrWhiteSpace(argument)
            ? null
            : Enum.TryParse(argument, ignoreCase: true, out EqCutMode mode) && Enum.IsDefined(mode)
                ? mode
                : throw new ArgumentException(
                    "MixerEqCutMode Argument, when set, must name a cut mode (Eq/Deep/Kill).", nameof(argument));

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

    private static ActionFeedbackState ValueFeedback(double value, string? argument = null)
        => new(IsActive: false, IsAvailable: true, Value: value, Argument: argument);

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
