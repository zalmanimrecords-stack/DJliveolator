using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Default mapper: on each message it finds the first matching binding, converts the value, and
/// dispatches the action. Unmapped messages are dropped quietly (MIDI is chatty), but a mapping
/// failure is logged with context and never silently swallowed (doc 05, global standards #16/#26).
/// </summary>
public sealed class ControllerMapper : IControllerMapper
{
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly ILogger<ControllerMapper> _logger;

    // Per-control soft-takeover state, keyed by the binding instance it belongs to. Keyed by
    // reference identity (not record value equality) so two distinct controls that happen to carry
    // identical field values keep independent pickup state. Reset whenever the profile is swapped.
    private readonly Dictionary<ControllerBinding, SoftTakeover> _takeovers =
        new(ReferenceEqualityComparer.Instance);

    // One-off pickup for a plain absolute control whose target automation moved (feedback RequiresPickup,
    // e.g. after an AUTO crossfade). Dropped the moment it picks up, so the control tracks directly again.
    private readonly Dictionary<ControllerBinding, SoftTakeover> _automationPickups =
        new(ReferenceEqualityComparer.Instance);

    public ControllerMapper(
        ControllerMappingProfile profile,
        IPerformanceActionDispatcher dispatcher,
        ILogger<ControllerMapper> logger)
    {
        ActiveProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ControllerMappingProfile ActiveProfile { get; private set; }

    /// <inheritdoc />
    public void SetProfile(ControllerMappingProfile profile)
    {
        ActiveProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        // The new profile's controls have never picked up their targets; drop stale pickup state.
        _takeovers.Clear();
        _automationPickups.Clear();
    }

    /// <inheritdoc />
    public void Apply(MidiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ControllerBinding? binding = FirstMatch(message);
        if (binding is null)
        {
            _logger.LogDebug("No binding for {Type} ch{Channel} d1={Data1} d2={Data2}.",
                message.Type, message.Channel, message.Data1, message.Data2);
            return;
        }

        try
        {
            double value = ControlValueConverter.ToActionValue(message, binding);

            if (binding.SoftTakeover && binding.InputMode == ActionInputMode.Absolute)
            {
                double current = _dispatcher.GetFeedback(binding.Action, binding.Slot).Value;
                SoftTakeoverResult takeover = TakeoverFor(binding).Evaluate(current, value);
                if (!takeover.PickedUp)
                {
                    // Hardware has not crossed the target yet — hold, do not jump the value.
                    _logger.LogTrace("Soft-takeover holding {Action} slot {Slot}; hw={Value} target={Target}.",
                        binding.Action, binding.Slot, value, current);
                    return;
                }

                value = takeover.Value;
            }
            // Only AUTO moves a target out from under the hardware, and only the crossfader — so only the
            // crossfader pays for a feedback query per tick (some are costly: macOS system volume spawns osascript).
            else if (binding.InputMode == ActionInputMode.Absolute
                     && binding.Action == PerformanceActionKind.MixerCrossfade
                     && !PickedUpAfterAutomation(binding, value))
            {
                return;
            }

            bool isPressed = !BindingMatcher.IsRelease(binding, message);
            _dispatcher.Dispatch(new PerformanceAction(
                binding.Action, binding.InputMode, value, binding.Slot, binding.Argument,
                IsPressed: isPressed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mapping failed for {Type} ch{Channel} d1={Data1} → {Action}.",
                message.Type, message.Channel, message.Data1, binding.Action);
        }
    }

    // Automation moved the target away from this physical control, so hold the control until it reaches the
    // new value rather than letting the first touch jump the mix back. Soft takeover for one hand-off only:
    // a crossfader's physical position is otherwise the truth (mappings/README.md).
    private bool PickedUpAfterAutomation(ControllerBinding binding, double value)
    {
        ActionFeedbackState target = _dispatcher.GetFeedback(binding.Action, binding.Slot);
        if (!target.RequiresPickup)
        {
            _automationPickups.Remove(binding);
            return true;
        }

        if (!_automationPickups.TryGetValue(binding, out SoftTakeover? pickup))
            _automationPickups[binding] = pickup = new SoftTakeover();

        if (!pickup.Evaluate(target.Value, value).PickedUp)
        {
            _logger.LogTrace("Holding {Action} slot {Slot} until it picks up after automation; hw={Value} target={Target}.",
                binding.Action, binding.Slot, value, target.Value);
            return false;
        }

        _automationPickups.Remove(binding);
        return true;
    }

    private SoftTakeover TakeoverFor(ControllerBinding binding)
    {
        if (!_takeovers.TryGetValue(binding, out SoftTakeover? takeover))
        {
            takeover = new SoftTakeover();
            _takeovers[binding] = takeover;
        }

        return takeover;
    }

    private ControllerBinding? FirstMatch(MidiMessage message)
    {
        foreach (ControllerBinding binding in ActiveProfile.Bindings)
        {
            if (BindingMatcher.Matches(binding, message))
                return binding;
        }

        return null;
    }
}
