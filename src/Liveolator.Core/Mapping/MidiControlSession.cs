using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Owns the live MIDI control pipeline at runtime: opens the configured controller, loads its mapping
/// profile, and wires the router (control), the feedback publisher (LEDs), and the activity monitor
/// (the shell's connection LED) onto one open input. This is the composition the App was missing —
/// previously the Settings tab only enumerated devices and nothing opened a controller (doc 05/12).
/// </summary>
/// <remarks>
/// Pure orchestration over Core seams (no UI, no native), so it unit-tests with fakes. Opening is
/// best-effort and tolerant: a missing/unmatched device, an absent native library, or a load error
/// leaves the session idle (logged) rather than crashing startup — mirroring the realtime audio
/// engine's graceful fallback. With no saved profile the pipeline still runs on an empty profile, so
/// the activity cue flashes on every message even before any mapping is authored (MIDI learn is a
/// separate increment). Device changes apply on the next start (the App composes from saved settings).
/// </remarks>
public sealed class MidiControlSession : IMidiControlStatus, IDisposable
{
    private readonly IMidiDeviceProvider _provider;
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly ILiveProfileStore _profileStore;
    private readonly IMidiLearnSession _learn;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MidiControlSession> _logger;

    private IMidiInput? _input;
    private IMidiOutput? _output;
    private ControllerMapper? _mapper;
    private MidiControllerRouter? _router;
    private MidiFeedbackPublisher? _feedback;
    private MidiActivityMonitor? _monitor;

    public MidiControlSession(
        IMidiDeviceProvider provider,
        IPerformanceActionDispatcher dispatcher,
        ILiveProfileStore profileStore,
        IMidiLearnSession learn,
        ILoggerFactory loggerFactory)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _learn = learn ?? throw new ArgumentNullException(nameof(learn));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<MidiControlSession>();
    }

    /// <summary>True once the controller input is open and routing.</summary>
    public bool IsInputConnected { get; private set; }

    /// <summary>True once a feedback (LED) output is open.</summary>
    public bool IsOutputConnected { get; private set; }

    /// <summary>The opened controller's device name, or null when idle.</summary>
    public string? InputDeviceName { get; private set; }

    /// <summary>The opened feedback device's name, or null when none.</summary>
    public string? OutputDeviceName { get; private set; }

    /// <summary>The profile currently routing (empty when none was saved for the device), or null when idle.</summary>
    public ControllerMappingProfile? ActiveProfile => _mapper?.ActiveProfile;

    /// <summary>
    /// Raised on each inbound MIDI message (re-raised from the activity monitor). Fires on the MIDI
    /// callback thread — UI subscribers must marshal to their own thread.
    /// </summary>
    public event EventHandler? ActivityDetected;

    /// <summary>
    /// Opens the controller named in <paramref name="settings"/> and builds the pipeline. Tears down
    /// any prior session first. No-op (stays idle) when no controller is selected; never throws.
    /// </summary>
    public async Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Stop();

        if (string.IsNullOrWhiteSpace(settings.ControllerInputName))
            return;

        try
        {
            IMidiInput? input = _provider.OpenInput(settings.ControllerInputName);
            if (input is null)
            {
                _logger.LogWarning("MIDI controller '{Device}' was not found; control is disabled.",
                    settings.ControllerInputName);
                return;
            }

            ControllerMappingProfile profile =
                await _profileStore.LoadMappingProfileAsync(input.DeviceName, cancellationToken).ConfigureAwait(false)
                ?? ControllerMappingProfile.Empty(input.DeviceName, input.DeviceName);

            var mapper = new ControllerMapper(profile, _dispatcher, _loggerFactory.CreateLogger<ControllerMapper>());
            var router = new MidiControllerRouter(input, mapper, _learn, _loggerFactory.CreateLogger<MidiControllerRouter>());
            var monitor = new MidiActivityMonitor(input);
            monitor.ActivityDetected += OnActivityDetected;

            TryOpenFeedback(settings.FeedbackOutputName, mapper);

            // Subscriptions (router + monitor) are attached before opening, so no early message is missed.
            input.Open();

            _input = input;
            _mapper = mapper;
            _router = router;
            _monitor = monitor;
            InputDeviceName = input.DeviceName;
            IsInputConnected = true;
            _logger.LogInformation("MIDI controller '{Device}' connected with profile '{Profile}' ({Count} binding(s)).",
                input.DeviceName, profile.Name, profile.Bindings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting the MIDI control session for '{Device}' failed.",
                settings.ControllerInputName);
            Stop();
        }
    }

    private void TryOpenFeedback(string? feedbackOutputName, ControllerMapper mapper)
    {
        if (string.IsNullOrWhiteSpace(feedbackOutputName))
            return;

        IMidiOutput? output = _provider.OpenOutput(feedbackOutputName);
        if (output is null)
        {
            _logger.LogWarning("MIDI feedback device '{Device}' was not found; LEDs are disabled.",
                feedbackOutputName);
            return;
        }

        _output = output;
        _feedback = new MidiFeedbackPublisher(_dispatcher, output, mapper, _loggerFactory.CreateLogger<MidiFeedbackPublisher>());
        OutputDeviceName = output.DeviceName;
        IsOutputConnected = true;
    }

    private void OnActivityDetected(object? sender, EventArgs e) => ActivityDetected?.Invoke(this, EventArgs.Empty);

    /// <summary>Tears the pipeline down and releases the devices; safe to call when already idle.</summary>
    public void Stop()
    {
        _router?.Dispose();
        _feedback?.Dispose();
        if (_monitor is not null)
        {
            _monitor.ActivityDetected -= OnActivityDetected;
            _monitor.Dispose();
        }

        if (_input is not null)
        {
            _input.Close();
            _input.Dispose();
        }
        _output?.Dispose();

        _router = null;
        _feedback = null;
        _monitor = null;
        _mapper = null;
        _input = null;
        _output = null;
        InputDeviceName = null;
        OutputDeviceName = null;
        IsInputConnected = false;
        IsOutputConnected = false;
    }

    public void Dispose() => Stop();
}
