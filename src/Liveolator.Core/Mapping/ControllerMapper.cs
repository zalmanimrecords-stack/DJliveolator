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
        => ActiveProfile = profile ?? throw new ArgumentNullException(nameof(profile));

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
            _dispatcher.Dispatch(new PerformanceAction(
                binding.Action, binding.InputMode, value, binding.Slot, binding.Argument));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mapping failed for {Type} ch{Channel} d1={Data1} → {Action}.",
                message.Type, message.Channel, message.Data1, binding.Action);
        }
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
