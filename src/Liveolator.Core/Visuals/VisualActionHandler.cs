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
        PerformanceActionKind.VisualToggleLayer,
        PerformanceActionKind.VisualSetLayerOpacity,
        PerformanceActionKind.VisualLaunchClip,
        PerformanceActionKind.VisualBlackout,
        PerformanceActionKind.VisualToggleStrobe,
        PerformanceActionKind.VisualTransitionNow,
        PerformanceActionKind.VisualTransitionNextBeat,
        PerformanceActionKind.VisualTransitionNextBar,
    };

    /// <summary>The transition style this handler requests. A later increment can carry it on the action.</summary>
    public const TransitionStyle DefaultTransition = TransitionStyle.Crossfade;

    private readonly IVisualPerformanceEngine _engine;
    private readonly ILogger<VisualActionHandler> _logger;

    // Blackout/strobe are boolean on the engine but arrive as momentary/toggle actions, so the
    // handler owns the latch and feeds it back for LED/UI state (mirrors BeatLock's lock latch).
    private bool _blackout;
    private bool _strobe;

    // The slot of the most recently loaded scene, so the Scene-Grid feedback can light the active
    // pad. -1 means nothing has been loaded yet. The engine does not expose an "active scene", so
    // the handler is the single owner of this UI/LED state.
    private int _activeSceneSlot = -1;

    public VisualActionHandler(IVisualPerformanceEngine engine, ILogger<VisualActionHandler>? logger = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger<VisualActionHandler>.Instance;
    }

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

    private void LaunchClip(PerformanceAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Argument))
        {
            _logger.LogWarning("VisualLaunchClip ignored: no clip id supplied in the action's Argument.");
            return;
        }

        _engine.LaunchClip(action.Slot, action.Argument, Quantize.Immediate);
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
