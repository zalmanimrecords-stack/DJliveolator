using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Visuals;

/// <summary>
/// The dispatcher handler that owns the visual actions (doc 04/08), translating them into
/// <see cref="IVisualPerformanceEngine"/> calls. It mirrors <see cref="BeatActionHandler"/>: it owns a
/// cohesive set of <see cref="PerformanceActionKind"/>s, drives one engine, and reports feedback
/// (active scene/bank, blackout/strobe latch state) so a Push/UI surface can follow it (doc 06/12).
///
/// Quantized transitions reuse the shared <see cref="Quantize"/> grid: the action kind selects the
/// quantum (<c>Now</c>/<c>NextBeat</c>/<c>NextBar</c>) and the engine resolves the actual fire time
/// against the one shared beat clock via <see cref="QuantizedLaunch"/> (doc 03/08) — the handler does
/// not double-resolve timing, keeping a single source of truth for the grid.
///
/// Blackout and strobe are momentary/toggle controls in the action vocabulary but boolean on the
/// engine, so the handler holds the on/off latch and reports it back as feedback.
/// </summary>
public sealed class VisualActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.VisualLoadScene,
        PerformanceActionKind.VisualSelectBank,
        PerformanceActionKind.VisualSetMacro,
        PerformanceActionKind.VisualSetLayerSource,
        PerformanceActionKind.VisualToggleLayer,
        PerformanceActionKind.VisualSetLayerOpacity,
        PerformanceActionKind.VisualLaunchClip,
        PerformanceActionKind.VisualBlackout,
        PerformanceActionKind.VisualToggleStrobe,
        PerformanceActionKind.VisualTransitionNow,
        PerformanceActionKind.VisualTransitionNextBeat,
        PerformanceActionKind.VisualTransitionNextBar,
        PerformanceActionKind.VisualLoadPreset,
        PerformanceActionKind.VisualSetLaunchQuantize,
    };

    /// <summary>The transition style this handler requests. A later increment can carry it on the action.</summary>
    public const TransitionStyle DefaultTransition = TransitionStyle.Crossfade;

    private readonly IVisualPerformanceEngine _engine;
    private readonly IGeneratorPresetRegistry? _presets;
    private readonly IVisualEffectRegistry? _effects;
    private readonly IBeatScheduler? _scheduler;
    private readonly ILogger<VisualActionHandler> _logger;

    // Blackout/strobe are boolean on the engine but arrive as momentary/toggle actions, so the
    // handler owns the latch and feeds it back for LED/UI state (mirrors BeatLock's lock latch).
    private bool _blackout;
    private bool _strobe;

    // The slot of the most recently loaded scene, so the Scene-Grid feedback can light the active
    // pad. -1 means nothing has been loaded yet. The engine does not expose an "active scene", so
    // the handler is the single owner of this UI/LED state.
    private int _activeSceneSlot = -1;

    public VisualActionHandler(
        IVisualPerformanceEngine engine,
        ILogger<VisualActionHandler>? logger = null,
        IGeneratorPresetRegistry? presets = null,
        IVisualEffectRegistry? effects = null,
        IBeatScheduler? scheduler = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger<VisualActionHandler>.Instance;
        _presets = presets;
        _effects = effects;
        _scheduler = scheduler;
    }

    /// <summary>
    /// How a scene-pad launch is quantized to the shared beat clock: <see cref="Quantize.Immediate"/>
    /// (off) loads the scene at once; <see cref="Quantize.NextBeat"/>/<see cref="Quantize.NextBar"/>
    /// snap it to the next boundary via the <see cref="IBeatScheduler"/> so a pad pressed mid-phrase
    /// drops on the beat/bar — the audio↔visual lock (doc 08). Defaults to off; the Live UI sets it.
    /// </summary>
    public Quantize LaunchQuantize { get; set; } = Quantize.Immediate;

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case PerformanceActionKind.VisualLoadScene:
                LoadScene(action.Slot);
                break;
            case PerformanceActionKind.VisualSelectBank:
                _engine.SelectBank(action.Slot);
                RaiseBankFeedback();
                break;
            case PerformanceActionKind.VisualSetMacro:
                SetMacro(action);
                break;
            case PerformanceActionKind.VisualSetLayerSource:
                SetLayerSource(action);
                break;
            case PerformanceActionKind.VisualToggleLayer:
                _engine.ToggleLayer(action.Slot);
                break;
            case PerformanceActionKind.VisualSetLayerOpacity:
                _engine.SetLayerOpacity(action.Slot, action.Value);
                break;
            case PerformanceActionKind.VisualLaunchClip:
                LaunchClip(action);
                break;
            case PerformanceActionKind.VisualBlackout:
                ToggleBlackout();
                break;
            case PerformanceActionKind.VisualToggleStrobe:
                ToggleStrobe();
                break;
            case PerformanceActionKind.VisualTransitionNow:
                _engine.Transition(DefaultTransition, Quantize.Immediate);
                break;
            case PerformanceActionKind.VisualTransitionNextBeat:
                _engine.Transition(DefaultTransition, Quantize.NextBeat);
                break;
            case PerformanceActionKind.VisualTransitionNextBar:
                _engine.Transition(DefaultTransition, Quantize.NextBar);
                break;
            case PerformanceActionKind.VisualLoadPreset:
                LoadPreset(action);
                break;
            case PerformanceActionKind.VisualSetLaunchQuantize:
                SetLaunchQuantize(action.Value);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => kind switch
        {
            PerformanceActionKind.VisualBlackout
                => new ActionFeedbackState(IsActive: _blackout, IsAvailable: true, Value: 0),
            PerformanceActionKind.VisualToggleStrobe
                => new ActionFeedbackState(IsActive: _strobe, IsAvailable: true, Value: 0),
            PerformanceActionKind.VisualLoadScene
                => new ActionFeedbackState(IsActive: slot == _activeSceneSlot, IsAvailable: HasScene(slot), Value: 0),
            PerformanceActionKind.VisualSelectBank
                => new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0),
            _ => ActionFeedbackState.Unavailable,
        };

    private void LoadScene(int slot)
    {
        VisualScene? scene = _engine.ActiveBank.Scene(slot);
        if (scene is null)
        {
            // Empty pad / out-of-range slot: surface, never silently swallow (global standard #26).
            _logger.LogWarning("VisualLoadScene ignored: bank '{Bank}' has no scene at slot {Slot}.",
                _engine.ActiveBank.Name, slot);
            return;
        }

        // Off (or no scheduler wired) loads now; otherwise defer the swap to the next beat/bar on the
        // shared clock so the scene drops in time (doc 08). The scheduler falls back to immediate when
        // the grid is not yet trustworthy, so a launch is never lost.
        if (LaunchQuantize == Quantize.Immediate || _scheduler is null)
            ApplyScene(scene, slot);
        else
            _scheduler.Schedule(LaunchQuantize, everyN: 1, () => ApplyScene(scene, slot));
    }

    private void ApplyScene(VisualScene scene, int slot)
    {
        _engine.LoadScene(scene, Quantize.Immediate);

        int previous = _activeSceneSlot;
        _activeSceneSlot = slot;

        // Release the previously-lit pad so the Scene Grid shows exactly one active scene.
        if (previous >= 0 && previous != slot)
            RaiseFeedback(
                PerformanceActionKind.VisualLoadScene, previous,
                new ActionFeedbackState(IsActive: false, IsAvailable: HasScene(previous), Value: 0));

        RaiseFeedback(
            PerformanceActionKind.VisualLoadScene, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
    }

    private void SetMacro(PerformanceAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Argument))
        {
            _logger.LogWarning("VisualSetMacro ignored: no macro name supplied in the action's Argument.");
            return;
        }

        _engine.SetMacro(action.Argument, action.Value);
        RaiseFeedback(
            PerformanceActionKind.VisualSetMacro,
            action.Slot,
            new ActionFeedbackState(
                IsActive: false,
                IsAvailable: true,
                Value: action.Value,
                Argument: action.Argument));
    }

    private void LoadPreset(PerformanceAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Argument))
        {
            _logger.LogWarning("VisualLoadPreset ignored: no preset id supplied in the action's Argument.");
            return;
        }
        if (_presets is null || _effects is null)
        {
            _logger.LogWarning(
                "VisualLoadPreset ignored: the preset/effect registries are not wired into this handler.");
            return;
        }
        if (!_presets.TryGet(action.Argument, out GeneratorPreset preset))
        {
            _logger.LogWarning("VisualLoadPreset ignored: no preset '{Preset}' is registered.", action.Argument);
            return;
        }
        if (!_effects.TryGet(preset.GeneratorEffectId, preset.GeneratorVersion, out VisualEffectDescriptor generator))
        {
            _logger.LogWarning(
                "VisualLoadPreset ignored: preset '{Preset}' references unknown generator '{Generator}'.",
                preset.PresetId, preset.GeneratorEffectId);
            return;
        }

        GeneratorPresetBinding binding;
        try
        {
            binding = GeneratorPresetExpansion.Expand(preset, generator, action.Slot);
        }
        catch (ArgumentException ex)
        {
            // A preset that survived registration but cannot expand against this descriptor (e.g. a
            // controllable id the generator no longer declares) is surfaced, never silently ignored.
            _logger.LogWarning(ex, "VisualLoadPreset ignored: preset '{Preset}' could not be expanded.", preset.PresetId);
            return;
        }

        _engine.LoadPreset(binding, action.Slot, Quantize.Immediate);
        RaiseFeedback(
            PerformanceActionKind.VisualLoadPreset,
            action.Slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: action.Argument));
    }

    private void LaunchClip(PerformanceAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Argument))
        {
            _logger.LogWarning("VisualLaunchClip ignored: no clip id supplied in the action's Argument.");
            return;
        }

        _engine.LaunchClip(action.Slot, action.Argument, Quantize.Immediate);
    }

    private void SetLayerSource(PerformanceAction action)
    {
        if (!VisualSourceActionCodec.TryDecode(action.Argument, out VisualSourceRef? source))
        {
            _logger.LogWarning("VisualSetLayerSource ignored: the action has no valid source.");
            return;
        }

        _engine.SetLayerSource(action.Slot, source!, Quantize.Immediate);
        RaiseFeedback(
            PerformanceActionKind.VisualSetLayerSource,
            action.Slot,
            new ActionFeedbackState(
                IsActive: true,
                IsAvailable: true,
                Value: 0,
                Argument: action.Argument));
    }

    private void SetLaunchQuantize(double value)
    {
        // 0 = off (immediate), 1 = next beat, 2 = next bar (doc 31). Thresholds tolerate a knob/fader
        // 0..1-ish source as well as discrete button values.
        LaunchQuantize = value switch
        {
            >= 1.5 => Quantize.NextBar,
            >= 0.5 => Quantize.NextBeat,
            _ => Quantize.Immediate,
        };
        RaiseFeedback(
            PerformanceActionKind.VisualSetLaunchQuantize, slot: 0,
            new ActionFeedbackState(
                IsActive: LaunchQuantize != Quantize.Immediate,
                IsAvailable: true,
                Value: (int)LaunchQuantize));
    }

    private void ToggleBlackout()
    {
        _blackout = !_blackout;
        _engine.Blackout(_blackout);
        RaiseFeedback(
            PerformanceActionKind.VisualBlackout, slot: 0,
            new ActionFeedbackState(IsActive: _blackout, IsAvailable: true, Value: 0));
    }

    private void ToggleStrobe()
    {
        _strobe = !_strobe;
        _engine.Strobe(_strobe);
        RaiseFeedback(
            PerformanceActionKind.VisualToggleStrobe, slot: 0,
            new ActionFeedbackState(IsActive: _strobe, IsAvailable: true, Value: 0));
    }

    private void RaiseBankFeedback()
        => RaiseFeedback(
            PerformanceActionKind.VisualSelectBank, slot: 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

    private bool HasScene(int slot) => _engine.ActiveBank.Scene(slot) is not null;
}
