using System.Collections.ObjectModel;
using System.Reactive;
using Liveolator.App.Shell;
using Liveolator.Core.Audio;
using Liveolator.Core.Extensions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
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
/// back to the system default / "(none)" rather than erroring. Applying these to a running audio engine
/// (re-init on the chosen device + buffer) is the next increment — this tab owns detection + persistence.
/// </remarks>
public sealed class SettingsViewModel : ViewModelBase
{
    /// <summary>Sentinel for "use the platform default output device" (persists as a null id).</summary>
    public static AudioOutputDevice SystemDefaultOutput { get; } =
        new(Id: "", Name: "System default", IsDefault: true);

    /// <summary>Sentinel list entry for "no MIDI device selected" (persists as a null name).</summary>
    public const string NoDevice = "(none)";

    private static readonly int[] BufferPresets = { 10, 20, 40, 60, 100, 150, 200 };

    private readonly IAudioOutputDeviceCatalog _outputs;
    private readonly IAudioCaptureDeviceCatalog _captures;
    private readonly IMidiDeviceProvider _midi;
    private readonly ISettingsStore _store;
    private readonly IExtensionCatalog? _extensions;
    private readonly IExtensionInstaller? _extensionInstaller;
    private readonly IUiThemeManager? _themes;
    private readonly IExtensionContentReloader? _contentReloader;
    private AppSettings _loadedSettings = AppSettings.Default;

    private AudioOutputDevice? _selectedOutputDevice;
    private int _selectedBufferMs = AudioSettings.DefaultBufferMs;
    private string _selectedMidiInput = NoDevice;
    private string _selectedMidiOutput = NoDevice;
    private string _status = string.Empty;
    private string _packagePath = string.Empty;
    private ExtensionItemViewModel? _selectedExtension;
    private bool _developerMode;
    private string? _activeUiThemeId;

    public SettingsViewModel(
        IAudioOutputDeviceCatalog outputs,
        IAudioCaptureDeviceCatalog captures,
        IMidiDeviceProvider midi,
        ISettingsStore store,
        IExtensionCatalog? extensions = null,
        IExtensionInstaller? extensionInstaller = null,
        IUiThemeManager? themes = null,
        IExtensionContentReloader? contentReloader = null)
    {
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _captures = captures ?? throw new ArgumentNullException(nameof(captures));
        _midi = midi ?? throw new ArgumentNullException(nameof(midi));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _extensions = extensions;
        _extensionInstaller = extensionInstaller;
        _themes = themes;
        _contentReloader = contentReloader;

        foreach (int ms in BufferPresets)
            BufferOptions.Add(ms);

        RefreshDevicesCommand = ReactiveCommand.Create(RefreshDevices);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        PreviewPackageCommand = ReactiveCommand.CreateFromTask(PreviewPackageAsync);
        InstallPackageCommand = ReactiveCommand.CreateFromTask(InstallPackageAsync);
        ToggleExtensionCommand = ReactiveCommand.CreateFromTask(ToggleExtensionAsync);
        UninstallExtensionCommand = ReactiveCommand.CreateFromTask(UninstallExtensionAsync);

        RefreshDevices();
    }

    /// <summary>Output devices to pick from, led by the <see cref="SystemDefaultOutput"/> sentinel.</summary>
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = new();

    /// <summary>Detected capture endpoints (informational; capture-source selection is a later seam).</summary>
    public ObservableCollection<AudioCaptureDevice> CaptureDevices { get; } = new();

    /// <summary>MIDI input device names, led by the <see cref="NoDevice"/> sentinel.</summary>
    public ObservableCollection<string> MidiInputDevices { get; } = new();

    /// <summary>MIDI output (feedback) device names, led by the <see cref="NoDevice"/> sentinel.</summary>
    public ObservableCollection<string> MidiOutputDevices { get; } = new();

    /// <summary>The selectable output buffer sizes in milliseconds.</summary>
    public ObservableCollection<int> BufferOptions { get; } = new();
    public ObservableCollection<ExtensionItemViewModel> InstalledExtensions { get; } = new();
    public ObservableCollection<string> UiThemeIds { get; } = new() { "Spartan" };

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

    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
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
        string previousMidiIn = SelectedMidiInput;
        string previousMidiOut = SelectedMidiOutput;

        OutputDevices.Clear();
        OutputDevices.Add(SystemDefaultOutput);
        foreach (AudioOutputDevice device in _outputs.EnumerateOutputDevices())
            OutputDevices.Add(device);

        CaptureDevices.Clear();
        foreach (AudioCaptureDevice device in _captures.EnumerateCaptureDevices())
            CaptureDevices.Add(device);

        FillMidi(MidiInputDevices, _midi.GetInputDeviceNames());
        FillMidi(MidiOutputDevices, _midi.GetOutputDeviceNames());

        // Keep the prior selection if it still exists; otherwise fall back to the safe default.
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == previousOutput?.Id) ?? SystemDefaultOutput;
        SelectedMidiInput = MidiInputDevices.Contains(previousMidiIn) ? previousMidiIn : NoDevice;
        SelectedMidiOutput = MidiOutputDevices.Contains(previousMidiOut) ? previousMidiOut : NoDevice;

        Status = $"Detected {OutputDevices.Count - 1} output device(s), "
               + $"{MidiInputDevices.Count - 1} MIDI input(s).";
    }

    /// <summary>Persists the current selections as normalized <see cref="AppSettings"/>.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var settings = _loadedSettings with
        {
            Audio = new AudioSettings
            {
                OutputDeviceId = string.IsNullOrEmpty(SelectedOutputDevice?.Id) ? null : SelectedOutputDevice!.Id,
                BufferMilliseconds = SelectedBufferMs,
            },
            Midi = new MidiSettings
            {
                ControllerInputName = SelectedMidiInput == NoDevice ? null : SelectedMidiInput,
                FeedbackOutputName = SelectedMidiOutput == NoDevice ? null : SelectedMidiOutput,
            },
            Extensions = new ExtensionSettings
            {
                DeveloperMode = DeveloperMode,
                ActiveUiThemeId = ActiveUiThemeId == "Spartan" ? null : ActiveUiThemeId,
            },
        };

        await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        _loadedSettings = settings.Normalized();
        Status = "Settings saved. UI theme changes apply after restart.";
    }

    private void ApplySettings(AppSettings settings)
    {
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == settings.Audio.OutputDeviceId)
            ?? SystemDefaultOutput;

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

        UiThemeIds.Clear();
        UiThemeIds.Add("Spartan");
        if (_themes is not null)
            foreach (UiThemeDefinition theme in _themes.AvailableThemes)
                if (!UiThemeIds.Contains(theme.Id))
                    UiThemeIds.Add(theme.Id);
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
