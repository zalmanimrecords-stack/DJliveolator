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

            _dispatcher.Dispatch(new PerformanceAction(
                binding.Action, binding.InputMode, value, binding.Slot, binding.Argument));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mapping failed for {Type} ch{Channel} d1={Data1} → {Action}.",
                message.Type, message.Channel, message.Data1, binding.Action);
        }
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
