using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Connects a MIDI input device to the mapping engine: each inbound message goes to the learn
/// session while it is armed (so binding a control does not also fire its action), and otherwise
/// to the mapper. The MIDI callback is wrapped so one bad message never tears down the input
/// stream (doc 05, global standards #16/#26).
/// </summary>
public sealed class MidiControllerRouter : IDisposable
{
    private readonly IMidiInput _input;
    private readonly IControllerMapper _mapper;
    private readonly IMidiLearnSession _learn;
    private readonly ILogger<MidiControllerRouter> _logger;
    private bool _disposed;

    public MidiControllerRouter(
        IMidiInput input,
        IControllerMapper mapper,
        IMidiLearnSession learn,
        ILogger<MidiControllerRouter> logger)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _learn = learn ?? throw new ArgumentNullException(nameof(learn));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _input.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, MidiMessage message)
    {
        if (message is null)
            return;

        // TEMP (doc 27 jog diagnostic — REMOVE after capture): log every raw inbound MIDI message so we
        // can see exactly what the CMD STUDIO 2A jog sends (CC#, data2 stream) and decode it correctly.
        _logger.LogInformation("[MIDI-RAW] {Type} ch{Channel} d1={Data1} d2={Data2}",
            message.Type, message.Channel, message.Data1, message.Data2);

        try
        {
            // During learn we capture the control rather than acting on it.
            if (_learn.IsArmed)
                _learn.Observe(message);
            else
                _mapper.Apply(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Routing MIDI message failed: {Type} ch{Channel} d1={Data1}.",
                message.Type, message.Channel, message.Data1);
        }
    }

    /// <summary>Detaches from the input so the router can be replaced on device change.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _input.MessageReceived -= OnMessageReceived;
        _disposed = true;
    }
}
