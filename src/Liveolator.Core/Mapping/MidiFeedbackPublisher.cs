using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Drives controller LEDs from action feedback: when the dispatcher reports a state change, every
/// binding in the active profile that targets that action/slot emits a MIDI message lighting its
/// control (doc 05 "Feedback / output", doc 06 Push LEDs). Reads the profile live through the
/// mapper so it follows profile swaps.
/// </summary>
public sealed class MidiFeedbackPublisher : IDisposable
{
    private const int FullOn = 127;

    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly IMidiOutput _output;
    private readonly IControllerMapper _mapper;
    private readonly ILogger<MidiFeedbackPublisher> _logger;
    private bool _disposed;

    public MidiFeedbackPublisher(
        IPerformanceActionDispatcher dispatcher,
        IMidiOutput output,
        IControllerMapper mapper,
        ILogger<MidiFeedbackPublisher> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _dispatcher.FeedbackChanged += OnFeedbackChanged;
    }

    private void OnFeedbackChanged(object? sender, ActionFeedbackChanged e)
    {
        try
        {
            foreach (ControllerBinding binding in _mapper.ActiveProfile.Bindings)
            {
                if (binding.Action != e.Kind || binding.Slot != e.Slot)
                    continue;
                if (TryBuildFeedbackMessage(binding, e.State, out MidiMessage message))
                    _output.Send(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publishing MIDI feedback for {Kind} slot {Slot} failed.", e.Kind, e.Slot);
        }
    }

    private static bool TryBuildFeedbackMessage(
        ControllerBinding binding, ActionFeedbackState state, out MidiMessage message)
    {
        message = default!;

        // Pitch bend is not an LED target.
        if (binding.TriggerType == MidiMessageType.PitchBend)
            return false;

        // Notes light on their NoteOn address regardless of which edge triggered the action.
        MidiMessageType type = binding.TriggerType == MidiMessageType.NoteOff
            ? MidiMessageType.NoteOn
            : binding.TriggerType;

        // Knob-backed controls reflect their value; on/off controls reflect the active flag.
        int value = binding.InputMode == ActionInputMode.Absolute
            ? (int)Math.Round(Math.Clamp(state.Value, 0, 1) * FullOn)
            : state.IsActive ? FullOn : 0;

        message = new MidiMessage(type, binding.Channel, binding.Data1, value);
        return true;
    }

    /// <summary>Stops driving feedback so the publisher can be replaced on device change.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _dispatcher.FeedbackChanged -= OnFeedbackChanged;
        _disposed = true;
    }
}
