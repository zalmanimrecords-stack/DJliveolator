using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Diagnostics;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Features.Mappings;
using Liveolator.App.Shell;
using Liveolator.App.Skins;
using Liveolator.App.Theme;
using Liveolator.Core.Audio;
using Liveolator.Core.Extensions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Skins;
using Liveolator.Media.Skins;
using ReactiveUI;

namespace Liveolator.App.Features.Settings;

/// <summary>
/// The Settings tab (doc 12). Detects the available audio output + MIDI equipment through the Core
/// seams (<see cref="IAudioOutputDeviceCatalog"/>, <see cref="IAudioCaptureDeviceCatalog"/>,
/// <see cref="IMidiDeviceProvider"/>), lets the performer pick the sound-card output, the output buffer
/// (latency vs. glitch-resistance), and the MIDI controller/feedback devices, then persists the choice
/// via <see cref="ISettingsStore"/>. Holds no Avalonia types and no native code — unit-tested with fakes.
/// </summary>
/// <remarks>
/// Selections are stored as device <b>ids/names</b> (not live handles), so they round-trip through the
/// settings file and survive re-plugging. A previously-selected device that is no longer present falls
/// back to the system default / "(none)" rather than erroring. On save the chosen output device + buffer
/// are applied to the running engine at runtime via <see cref="AudioReinitCoordinator"/> (re-open without
/// a restart, rolling back on failure), and the capture source is applied via
/// <see cref="ICaptureSourceController"/>; both are optional (null in catalog-browser mode with no
/// realtime engine), so the tab still detects + persists headless.
/// </remarks>
public sealed class SettingsViewModel : ViewModelBase
{
    /// <summary>Sentinel for "use the platform default output device" (persists as a null id).</summary>
    public static AudioOutputDevice SystemDefaultOutput { get; } =
        new(Id: "", Name: "System default", IsDefault: true);

    /// <summary>Sentinel capture entry for "no live capture source" (persists as a null id + kind).</summary>
    public static AudioCaptureDevice NoCaptureSource { get; } =
        new(Id: "", Name: "(none)", Kind: CaptureSourceKind.LineInput, IsDefault: false);

    /// <summary>Sentinel list entry for "no MIDI device selected" (persists as a null name).</summary>
    public const string NoDevice = "(none)";

    private static readonly int[] BufferPresets = { 10, 20, 40, 60, 100, 150, 200 };

    private readonly IAudioOutputDeviceCatalog _outputs;
    private readonly IAudioCaptureDeviceCatalog _captures;
    private readonly IMidiDeviceProvider _midi;
    private readonly ISettingsStore _store;
    private readonly AudioReinitCoordinator? _audioReinit;
    private readonly ICaptureSourceController? _captureController;
    private readonly IMidiControlSession? _midiControlSession;
    private readonly IExtensionCatalog? _extensions;
    private readonly IExtensionInstaller? _extensionInstaller;
    private readonly IUiThemeManager? _themes;
    private readonly IExtensionContentReloader? _contentReloader;
    private readonly PerformanceDeckSet? _decks;
    private readonly ILogFileLocator? _logLocator;
    private readonly IControlSkinCatalog? _controlSkins;
    private readonly IControlSkinApplier? _controlSkinApplier;
    private readonly IUiThemeLiveApplier? _uiThemeLiveApplier;
    private AppSettings _loadedSettings = AppSettings.Default;

    private AudioOutputDevice? _selectedOutputDevice;
    private int _selectedBufferMs = AudioSettings.DefaultBufferMs;
    private AudioCaptureDevice? _selectedCaptureDevice;
    private string _selectedMidiInput = NoDevice;
    private string _selectedMidiOutput = NoDevice;
    private string _status = string.Empty;
    private string _packagePath = string.Empty;
    private ExtensionItemViewModel? _selectedExtension;
    private bool _developerMode;
    private string? _activeUiThemeId;
    private string? _activeKnobSkinId;
    private string? _activeSliderSkinId;
    private double _waveformZoomSeconds = VisualsSettings.DefaultZoomSeconds;
    private double _nudgeSeconds = VisualsSettings.DefaultNudgeSeconds;
    private string _selectedLogLevel = DiagnosticsSettings.DefaultMinimumLevel;

    public SettingsViewModel(
        IAudioOutputDeviceCatalog outputs,
        IAudioCaptureDeviceCatalog captures,
        IMidiDeviceProvider midi,
        ISettingsStore store,
        AudioReinitCoordinator? audioReinit = null,
        ICaptureSourceController? captureController = null,
        IMidiControlSession? midiControlSession = null,
        IExtensionCatalog? extensions = null,
        IExtensionInstaller? extensionInstaller = null,
        IUiThemeManager? themes = null,
        IExtensionContentReloader? contentReloader = null,
        PerformanceDeckSet? decks = null,
        ILogFileLocator? logLocator = null,
        IControlSkinCatalog? controlSkins = null,
        IControlSkinApplier? controlSkinApplier = null,
        IUiThemeLiveApplier? uiThemeLiveApplier = null,
        MappingsViewModel? mappings = null)
    {
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _captures = captures ?? throw new ArgumentNullException(nameof(captures));
        _midi = midi ?? throw new ArgumentNullException(nameof(midi));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _audioReinit = audioReinit;
        _captureController = captureController;
        _midiControlSession = midiControlSession;
        _extensions = extensions;
        _extensionInstaller = extensionInstaller;
        _themes = themes;
        _contentReloader = contentReloader;
        _decks = decks;
        _logLocator = logLocator;
        _controlSkins = controlSkins;
        _controlSkinApplier = controlSkinApplier;
        _uiThemeLiveApplier = uiThemeLiveApplier;
        Mappings = mappings;

        foreach (int ms in BufferPresets)
            BufferOptions.Add(ms);
        foreach (string level in DiagnosticsSettings.Levels)
            LogLevels.Add(level);

        RefreshDevicesCommand = ReactiveCommand.Create(RefreshDevices);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        ApplyThemeCommand = ReactiveCommand.Create(ApplyTheme);
        PreviewPackageCommand = ReactiveCommand.CreateFromTask(PreviewPackageAsync);
        InstallPackageCommand = ReactiveCommand.CreateFromTask(InstallPackageAsync);
        ToggleExtensionCommand = ReactiveCommand.CreateFromTask(ToggleExtensionAsync);
        UninstallExtensionCommand = ReactiveCommand.CreateFromTask(UninstallExtensionAsync);
        OpenLogsFolderCommand = ReactiveCommand.Create(
            () => _logLocator?.RevealInFileManager(),
            Observable.Return(_logLocator is not null));

        RefreshDevices();
    }

    /// <summary>Output devices to pick from, led by the <see cref="SystemDefaultOutput"/> sentinel.</summary>
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = new();

    /// <summary>Selectable capture sources, led by the <see cref="NoCaptureSource"/> "(none)" sentinel.</summary>
    public ObservableCollection<AudioCaptureDevice> CaptureDevices { get; } = new();

    /// <summary>MIDI input device names, led by the <see cref="NoDevice"/> sentinel.</summary>
    public ObservableCollection<string> MidiInputDevices { get; } = new();

    /// <summary>MIDI output (feedback) device names, led by the <see cref="NoDevice"/> sentinel.</summary>
    public ObservableCollection<string> MidiOutputDevices { get; } = new();

    /// <summary>The selectable output buffer sizes in milliseconds.</summary>
    public ObservableCollection<int> BufferOptions { get; } = new();
    public ObservableCollection<ExtensionItemViewModel> InstalledExtensions { get; } = new();
    public ObservableCollection<string> UiThemeIds { get; } = new() { "Spartan" };

    /// <summary>Sentinel list entry for "use the built-in control look" (persists as a null skin id).</summary>
    public const string NoSkin = "(built-in)";

    /// <summary>Available knob-skin ids (doc 30), led by the <see cref="NoSkin"/> sentinel.</summary>
    public ObservableCollection<string> KnobSkinIds { get; } = new() { NoSkin };

    /// <summary>Available slider-skin ids (doc 30), led by the <see cref="NoSkin"/> sentinel.</summary>
    public ObservableCollection<string> SliderSkinIds { get; } = new() { NoSkin };

    /// <summary>The selectable log verbosity levels (least to most severe).</summary>
    public ObservableCollection<string> LogLevels { get; } = new();

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedOutputDevice, value);
    }

    public int SelectedBufferMs
    {
        get => _selectedBufferMs;
        set => this.RaiseAndSetIfChanged(ref _selectedBufferMs, value);
    }

    public AudioCaptureDevice? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedCaptureDevice, value);
    }

    public string SelectedMidiInput
    {
        get => _selectedMidiInput;
        set => this.RaiseAndSetIfChanged(ref _selectedMidiInput, value);
    }

    public string SelectedMidiOutput
    {
        get => _selectedMidiOutput;
        set => this.RaiseAndSetIfChanged(ref _selectedMidiOutput, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string PackagePath
    {
        get => _packagePath;
        set => this.RaiseAndSetIfChanged(ref _packagePath, value);
    }

    public ExtensionItemViewModel? SelectedExtension
    {
        get => _selectedExtension;
        set => this.RaiseAndSetIfChanged(ref _selectedExtension, value);
    }

    public bool DeveloperMode
    {
        get => _developerMode;
        set => this.RaiseAndSetIfChanged(ref _developerMode, value);
    }

    public string? ActiveUiThemeId
    {
        get => _activeUiThemeId;
        set => this.RaiseAndSetIfChanged(ref _activeUiThemeId, value);
    }

    /// <summary>Selected knob skin id (doc 30), or <see cref="NoSkin"/> for the built-in look. Applied live on Save.</summary>
    public string? ActiveKnobSkinId
    {
        get => _activeKnobSkinId;
        set => this.RaiseAndSetIfChanged(ref _activeKnobSkinId, value);
    }

    /// <summary>Selected slider skin id (doc 30), or <see cref="NoSkin"/> for the built-in look. Applied live on Save.</summary>
    public string? ActiveSliderSkinId
    {
        get => _activeSliderSkinId;
        set => this.RaiseAndSetIfChanged(ref _activeSliderSkinId, value);
    }

    /// <summary>Deck waveform zoom — seconds of audio shown in the zoomed (playing) view; lower = more
    /// zoomed in. Persisted on Save and applied to both decks live.</summary>
    public double WaveformZoomSeconds
    {
        get => _waveformZoomSeconds;
        set => this.RaiseAndSetIfChanged(ref _waveformZoomSeconds, value);
    }

    /// <summary>Slider lower bound (seconds) for <see cref="WaveformZoomSeconds"/> — the most zoomed-in.</summary>
    public double WaveformZoomMin => VisualsSettings.MinZoomSeconds;

    /// <summary>Slider upper bound (seconds) for <see cref="WaveformZoomSeconds"/> — the least zoomed-in.</summary>
    public double WaveformZoomMax => VisualsSettings.MaxZoomSeconds;

    /// <summary>Seconds the deck track-nudge buttons (◄ / ►) move the playhead per press. Persisted on
    /// Save and applied to both decks live.</summary>
    public double NudgeSeconds
    {
        get => _nudgeSeconds;
        set => this.RaiseAndSetIfChanged(ref _nudgeSeconds, value);
    }

    /// <summary>Slider bounds (seconds) for <see cref="NudgeSeconds"/>.</summary>
    public double NudgeMin => VisualsSettings.MinNudgeSeconds;
    public double NudgeMax => VisualsSettings.MaxNudgeSeconds;

    /// <summary>The persisted log verbosity. Applied to the file log on the next launch (the sink is built
    /// at startup); persisted on Save.</summary>
    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set => this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
    }

    /// <summary>The absolute path of the active log file, shown so the performer can find it for support.</summary>
    public string LogFilePath => _logLocator?.CurrentFilePath ?? "(file logging unavailable)";

    /// <summary>True when a log file exists to open (false in headless/test composition).</summary>
    public bool CanOpenLogs => _logLocator is not null;

    /// <summary>The MIDI mapping / learn surface, embedded in the MIDI settings tab (null in headless tests).</summary>
    public MappingsViewModel? Mappings { get; }

    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLogsFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewPackageCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallPackageCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleExtensionCommand { get; }
    public ReactiveCommand<Unit, Unit> UninstallExtensionCommand { get; }

    /// <summary>Loads the persisted settings and selects the matching detected devices.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        _loadedSettings = settings;
        RefreshDevices();
        ApplySettings(settings);
        await RefreshExtensionsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-enumerates all equipment (the "detect" action), preserving current selections where possible.</summary>
    public void RefreshDevices()
    {
        AudioOutputDevice? previousOutput = SelectedOutputDevice;
        AudioCaptureDevice? previousCapture = SelectedCaptureDevice;
        string previousMidiIn = SelectedMidiInput;
        string previousMidiOut = SelectedMidiOutput;

        OutputDevices.Clear();
        OutputDevices.Add(SystemDefaultOutput);
        foreach (AudioOutputDevice device in _outputs.EnumerateOutputDevices())
            OutputDevices.Add(device);

        CaptureDevices.Clear();
        CaptureDevices.Add(NoCaptureSource);
        foreach (AudioCaptureDevice device in _captures.EnumerateCaptureDevices())
            CaptureDevices.Add(device);

        FillMidi(MidiInputDevices, _midi.GetInputDeviceNames());
        FillMidi(MidiOutputDevices, _midi.GetOutputDeviceNames());

        // Keep the prior selection if it still exists; otherwise fall back to the safe default.
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == previousOutput?.Id) ?? SystemDefaultOutput;
        SelectedCaptureDevice = MatchCapture(previousCapture);
        SelectedMidiInput = MidiInputDevices.Contains(previousMidiIn) ? previousMidiIn : NoDevice;
        SelectedMidiOutput = MidiOutputDevices.Contains(previousMidiOut) ? previousMidiOut : NoDevice;

        Status = $"Detected {OutputDevices.Count - 1} output device(s), "
               + $"{MidiInputDevices.Count - 1} MIDI input(s).";
    }

    /// <summary>
    /// Persists the current selections as normalized <see cref="AppSettings"/>, then applies the audio
    /// output device/buffer to the running engine at runtime (re-init, rolling back on failure) and the
    /// chosen capture source, surfacing the outcome in <see cref="Status"/>.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        bool captureSelected = SelectedCaptureDevice is { } cap && !string.IsNullOrEmpty(cap.Id);
        var audio = new AudioSettings
        {
            OutputDeviceId = string.IsNullOrEmpty(SelectedOutputDevice?.Id) ? null : SelectedOutputDevice!.Id,
            BufferMilliseconds = SelectedBufferMs,
            CaptureDeviceId = captureSelected ? SelectedCaptureDevice!.Id : null,
            CaptureSource = captureSelected ? SelectedCaptureDevice!.Kind : null,
        };
        var settings = _loadedSettings with
        {
            Audio = audio,
            Midi = new MidiSettings
            {
                ControllerInputName = SelectedMidiInput == NoDevice ? null : SelectedMidiInput,
                FeedbackOutputName = SelectedMidiOutput == NoDevice ? null : SelectedMidiOutput,
            },
            Extensions = new ExtensionSettings
            {
                DeveloperMode = DeveloperMode,
                ActiveUiThemeId = ActiveUiThemeId == "Spartan" ? null : ActiveUiThemeId,
                ActiveKnobSkinId = ActiveKnobSkinId == NoSkin ? null : ActiveKnobSkinId,
                ActiveSliderSkinId = ActiveSliderSkinId == NoSkin ? null : ActiveSliderSkinId,
            },
            Visuals = new VisualsSettings(WaveformZoomSeconds, NudgeSeconds),
            Diagnostics = new DiagnosticsSettings(SelectedLogLevel),
        };

        await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        _loadedSettings = settings.Normalized();
        // Re-skin the live controls so an authored knob/slider look takes effect without a restart (doc 30).
        ApplyControlSkins(_loadedSettings.Extensions);
        // Apply the (normalized) zoom + nudge step to the live decks so the change takes effect without a restart.
        _decks?.SetWaveformZoom(_loadedSettings.Visuals.WaveformZoomSeconds);
        _decks?.SetNudgeSeconds(_loadedSettings.Visuals.NudgeSeconds);
        string engineStatus = ApplyToRunningEngine(settings.Audio.Normalized());
        string midiStatus = await ApplyMidiAsync(_loadedSettings.Midi, cancellationToken).ConfigureAwait(false);
        Status = string.IsNullOrEmpty(midiStatus) ? engineStatus : $"{engineStatus} {midiStatus}";
    }

    private async Task<string> ApplyMidiAsync(MidiSettings settings, CancellationToken cancellationToken)
    {
        if (_midiControlSession is null)
            return string.Empty;

        await _midiControlSession.StartAsync(settings, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.ControllerInputName))
            return "MIDI controller disconnected.";

        return _midiControlSession.IsInputConnected
            ? "MIDI controller connected."
            : "MIDI controller could not be opened.";
    }

    // Applies the saved audio choice to the live engine (when present) and reports a performer-facing
    // status. Re-init/capture are optional seams — in catalog-browser mode (no realtime engine) the
    // choice is saved for next launch only.
    private string ApplyToRunningEngine(AudioSettings audio)
    {
        if (_audioReinit is null && _captureController is null)
            return "Settings saved (applied on next launch).";

        var parts = new List<string> { "Settings saved." };

        if (_audioReinit is not null)
        {
            AudioReinitResult result = _audioReinit.Apply(audio);
            parts.Add(result switch
            {
                AudioReinitResult.Reinitialized => "Audio device re-initialised.",
                AudioReinitResult.RolledBack => "Audio device change failed — kept the previous device.",
                _ => string.Empty, // NoChange: nothing to report
            });
        }

        if (_captureController is not null)
        {
            AudioCaptureDevice? device =
                SelectedCaptureDevice is { } d && !string.IsNullOrEmpty(d.Id) ? d : null;
            if (!_captureController.SelectCaptureSource(device))
                parts.Add("Capture source could not be opened.");
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private void ApplySettings(AppSettings settings)
    {
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == settings.Audio.OutputDeviceId)
            ?? SystemDefaultOutput;

        // A persisted capture device that is gone (unplugged) falls back to "(none)" rather than erroring.
        SelectedCaptureDevice = string.IsNullOrEmpty(settings.Audio.CaptureDeviceId)
            ? NoCaptureSource
            : CaptureDevices.FirstOrDefault(d => d.Id == settings.Audio.CaptureDeviceId) ?? NoCaptureSource;

        // A persisted buffer may not be one of the presets (older config / hand-edit) — make it selectable.
        int buffer = settings.Audio.BufferMilliseconds;
        if (!BufferOptions.Contains(buffer))
            InsertSorted(BufferOptions, buffer);
        SelectedBufferMs = buffer;

        SelectedMidiInput = settings.Midi.ControllerInputName is { } input && MidiInputDevices.Contains(input)
            ? input : NoDevice;
        SelectedMidiOutput = settings.Midi.FeedbackOutputName is { } output && MidiOutputDevices.Contains(output)
            ? output : NoDevice;
        DeveloperMode = settings.Extensions.DeveloperMode;
        ActiveUiThemeId = settings.Extensions.ActiveUiThemeId ?? "Spartan";
        WaveformZoomSeconds = settings.Visuals.WaveformZoomSeconds;
        NudgeSeconds = settings.Visuals.NudgeSeconds;
        SelectedLogLevel = settings.Diagnostics.Normalized().MinimumLevel;

        UiThemeIds.Clear();
        UiThemeIds.Add("Spartan");
        if (_themes is not null)
            foreach (UiThemeDefinition theme in _themes.AvailableThemes)
                if (!UiThemeIds.Contains(theme.Id))
                    UiThemeIds.Add(theme.Id);

        // Populate the skin pickers BEFORE selecting, so a persisted id is present in its list (else the
        // ComboBox would reset the selection when its items change).
        PopulateSkinIds();
        ActiveKnobSkinId = SkinIdOrDefault(KnobSkinIds, settings.Extensions.ActiveKnobSkinId);
        ActiveSliderSkinId = SkinIdOrDefault(SliderSkinIds, settings.Extensions.ActiveSliderSkinId);
    }

    private void PopulateSkinIds()
    {
        KnobSkinIds.Clear();
        KnobSkinIds.Add(NoSkin);
        SliderSkinIds.Clear();
        SliderSkinIds.Add(NoSkin);
        if (_controlSkins is null)
            return;

        foreach (LoadedControlSkin skin in _controlSkins.Skins)
        {
            ObservableCollection<string> target =
                string.Equals(skin.File.Kind, ControlSkinKind.Slider, StringComparison.OrdinalIgnoreCase)
                    ? SliderSkinIds
                    : KnobSkinIds;
            if (!target.Contains(skin.SkinId))
                target.Add(skin.SkinId);
        }
    }

    // A persisted skin id that is no longer installed falls back to the built-in look rather than erroring.
    private static string SkinIdOrDefault(ObservableCollection<string> available, string? persisted)
        => persisted is not null && available.Contains(persisted) ? persisted : NoSkin;

    private void ApplyControlSkins(ExtensionSettings extensions)
    {
        if (_controlSkinApplier is null)
            return;
        _controlSkinApplier.Apply(ResolveSkin(extensions.ActiveKnobSkinId), ResolveSkin(extensions.ActiveSliderSkinId));
    }

    private ControlSkinFile? ResolveSkin(string? skinId)
        => skinId is not null && _controlSkins is not null && _controlSkins.TryGet(skinId, out ControlSkinFile skin)
            ? skin
            : null;

    // "Apply" button (doc 30): load the selected UI theme into the live app without a restart. Re-applies the
    // current control skins on top so an active knob/slider skin still overrides the theme's control colours.
    // Selecting "Spartan" resolves the built-in default theme, which resets every token (and clears any image).
    private void ApplyTheme()
    {
        if (_uiThemeLiveApplier is null || _themes is null)
            return;
        if (ActiveUiThemeId is not { } id || !_themes.TryGet(id, out UiThemeDefinition theme))
        {
            Status = "No theme selected.";
            return;
        }

        _uiThemeLiveApplier.Apply(theme);
        _controlSkinApplier?.Apply(
            ResolveSkin(ActiveKnobSkinId == NoSkin ? null : ActiveKnobSkinId),
            ResolveSkin(ActiveSliderSkinId == NoSkin ? null : ActiveSliderSkinId));
        Status = $"Applied theme '{theme.Name}'. Save to keep it for next launch.";
    }

    // Re-matches the prior capture selection after a re-enumeration: the "(none)" sentinel stays
    // "(none)", an existing device by id is kept, and a vanished device falls back to "(none)".
    private AudioCaptureDevice MatchCapture(AudioCaptureDevice? previous)
    {
        if (previous is null || string.IsNullOrEmpty(previous.Id))
            return NoCaptureSource;
        return CaptureDevices.FirstOrDefault(d => d.Id == previous.Id) ?? NoCaptureSource;
    }

    private static void FillMidi(ObservableCollection<string> target, IReadOnlyList<string> names)
    {
        target.Clear();
        target.Add(NoDevice);
        foreach (string name in names)
            target.Add(name);
    }

    private static void InsertSorted(ObservableCollection<int> options, int value)
    {
        int index = 0;
        while (index < options.Count && options[index] < value)
            index++;
        options.Insert(index, value);
    }

    private async Task PreviewPackageAsync()
    {
        if (_extensionInstaller is null || string.IsNullOrWhiteSpace(PackagePath))
        {
            Status = "Choose a .liveolator-pack path first.";
            return;
        }
        ExtensionInstallPreview preview = await _extensionInstaller.PreviewAsync(PackagePath);
        Status = preview.Validation.IsValid
            ? $"Valid package: {preview.Validation.Manifest!.PackageId} {preview.Validation.Manifest.Version}, "
              + $"{preview.Entries.Count} file(s)."
            : string.Join(" ", preview.Validation.Issues.Select(i => i.Message));
    }

    private async Task InstallPackageAsync()
    {
        if (_extensionInstaller is null || string.IsNullOrWhiteSpace(PackagePath))
        {
            Status = "Choose a .liveolator-pack path first.";
            return;
        }
        try
        {
            InstalledExtension installed = await _extensionInstaller.InstallAsync(PackagePath);
            if (_contentReloader is not null)
                await _contentReloader.ReloadAsync();
            await RefreshExtensionsAsync();
            Status = $"Installed {installed.Manifest.PackageId} {installed.Manifest.Version}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            Status = $"Install failed: {ex.Message}";
        }
    }

    private async Task ToggleExtensionAsync()
    {
        if (_extensionInstaller is null || SelectedExtension is null)
            return;
        await _extensionInstaller.SetEnabledAsync(
            SelectedExtension.PackageId,
            SelectedExtension.Version,
            !SelectedExtension.IsEnabled);
        if (_contentReloader is not null)
            await _contentReloader.ReloadAsync();
        await RefreshExtensionsAsync();
        Status = "Extension state updated.";
    }

    private async Task UninstallExtensionAsync()
    {
        if (_extensionInstaller is null || SelectedExtension is null)
            return;
        await _extensionInstaller.UninstallAsync(SelectedExtension.PackageId, SelectedExtension.Version);
        if (_contentReloader is not null)
            await _contentReloader.ReloadAsync();
        await RefreshExtensionsAsync();
        Status = "Extension uninstalled.";
    }

    private async Task RefreshExtensionsAsync(CancellationToken cancellationToken = default)
    {
        if (_extensions is null)
            return;
        await _extensions.RefreshAsync(cancellationToken);
        InstalledExtensions.Clear();
        foreach (InstalledExtension extension in _extensions.Installed)
            InstalledExtensions.Add(new ExtensionItemViewModel(extension));
        SelectedExtension = InstalledExtensions.FirstOrDefault();
    }
}
