using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Mapping;

/// <summary>
/// The composed, live MIDI input pipeline: an opened <see cref="IMidiInput"/> driven through a
/// <see cref="MidiControllerRouter"/> → <see cref="ControllerMapper"/> → the one
/// <see cref="IPerformanceActionDispatcher"/>, with profile auto-selection by device name and
/// (optionally) <see cref="MidiFeedbackPublisher"/> driving LEDs back out an <see cref="IMidiOutput"/>.
/// </summary>
/// <remarks>
/// This is the pure-managed wiring the App's composition root uses so the chosen hardware controller
/// drives the dispatcher (doc 05/12). It owns the pieces it builds and tears them all down on
/// <see cref="Dispose"/>, so the App can replace the pipeline on a device change. It does NOT open or
/// close the device list / native library — the caller opens the device (in the binding project) and
/// hands it in, keeping Core free of native dependencies and fully unit-testable with fakes.
/// </remarks>
public sealed class MidiInputPipeline : IDisposable
{
    private readonly IMidiInput _input;
    private readonly MidiControllerRouter _router;
    private readonly MidiFeedbackPublisher? _feedback;
    private bool _disposed;

    private MidiInputPipeline(
        IMidiInput input,
        IControllerMapper mapper,
        IMidiLearnSession learnSession,
        MidiControllerRouter router,
        MidiFeedbackPublisher? feedback)
    {
        _input = input;
        Mapper = mapper;
        LearnSession = learnSession;
        _router = router;
        _feedback = feedback;
    }

    /// <summary>The mapper driving the dispatcher; the UI swaps its profile on user selection.</summary>
    public IControllerMapper Mapper { get; }

    /// <summary>The learn session armed by the Mappings UI to capture/override a binding.</summary>
    public IMidiLearnSession LearnSession { get; }

    /// <summary>The profile currently in effect (the auto-selected one at creation).</summary>
    public ControllerMappingProfile ActiveProfile => Mapper.ActiveProfile;

    /// <summary>
    /// Composes and starts the pipeline over an already-opened <paramref name="input"/>. The profile is
    /// auto-selected from <paramref name="profiles"/> by matching <paramref name="input"/>'s device name
    /// (<see cref="MidiProfileSelector"/>); when none match it arms with an empty profile so input still
    /// flows (learn mode can capture) but nothing is mis-mapped. When <paramref name="output"/> is
    /// supplied, LED feedback is published back to it; without it, control still works (doc 06).
    /// </summary>
    public static MidiInputPipeline Create(
        IMidiInput input,
        IMidiOutput? output,
        IPerformanceActionDispatcher dispatcher,
        IEnumerable<ControllerMappingProfile> profiles,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        IReadOnlyList<ControllerMappingProfile> profileList = profiles.ToList();
        ControllerMappingProfile profile =
            MidiProfileSelector.Select(input.DeviceName, profileList)
            ?? ControllerMappingProfile.Empty($"{input.DeviceName} (unmapped)", input.DeviceName);

        var mapper = new ControllerMapper(profile, dispatcher, loggerFactory.CreateLogger<ControllerMapper>());
        var learnSession = new MidiLearnSession();
        var router = new MidiControllerRouter(
            input, mapper, learnSession, loggerFactory.CreateLogger<MidiControllerRouter>());

        MidiFeedbackPublisher? feedback = output is null
            ? null
            : new MidiFeedbackPublisher(
                dispatcher, output, mapper, loggerFactory.CreateLogger<MidiFeedbackPublisher>());

        // Open last, after routing is attached, so no message is missed between open and subscribe.
        input.Open();

        return new MidiInputPipeline(input, mapper, learnSession, router, feedback);
    }

    /// <summary>
    /// Tears the pipeline down: stops feedback, detaches routing, closes and disposes the input. Safe
    /// to call more than once. The dispatcher and profiles outlive the pipeline (owned by the host).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _feedback?.Dispose();
        _router.Dispose();
        _input.Close();
        _input.Dispose();
        _disposed = true;
    }
}
