namespace Liveolator.Core.Settings;

/// <summary>
/// The persisted application preferences (doc 12 Settings tab): the realtime audio output choice and
/// the MIDI controller choice. One aggregate so the Settings UI loads/saves a single object and the
/// composition root reads one contract. Pure data — persisted via <c>ISettingsStore</c>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Realtime audio output settings (device + buffer).</summary>
    public AudioSettings Audio { get; init; } = AudioSettings.Default;

    /// <summary>MIDI controller settings (input + feedback device names).</summary>
    public MidiSettings Midi { get; init; } = MidiSettings.Default;

    /// <summary>Extension developer-mode and the UI theme selected for the next startup.</summary>
    public ExtensionSettings Extensions { get; init; } = ExtensionSettings.Default;

    /// <summary>Visual/UI preferences (e.g. the deck waveform zoom).</summary>
    public VisualsSettings Visuals { get; init; } = VisualsSettings.Default;

    /// <summary>Diagnostics/logging preferences (the on-disk log verbosity).</summary>
    public DiagnosticsSettings Diagnostics { get; init; } = DiagnosticsSettings.Default;

    /// <summary>Per-add-on preferences (e.g. the custom VU-meter face image).</summary>
    public AddonSettings Addons { get; init; } = AddonSettings.Default;

    /// <summary>The default preferences (system audio device, default buffer, no controller).</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>Returns a copy with every section normalized (buffer clamped, blank names folded).</summary>
    public AppSettings Normalized()
        => this with
        {
            Audio = Audio.Normalized(),
            Midi = Midi.Normalized(),
            Extensions = Extensions.Normalized(),
            Visuals = Visuals.Normalized(),
            Diagnostics = Diagnostics.Normalized(),
            Addons = Addons.Normalized(),
        };
}
