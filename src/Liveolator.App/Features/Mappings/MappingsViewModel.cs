using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Mappings;

public sealed class MappingsViewModel : ViewModelBase, IDisposable
{
    private readonly IMidiControlSession _session;
    private readonly ILiveProfileStore? _profileStore;
    private readonly IMappingProfilePortability? _portability;
    private readonly IMappingFilePicker? _filePicker;
    private MappingTargetViewModel? _selectedTarget;
    private MappingBindingViewModel? _selectedBinding;
    private string _status = string.Empty;

    public MappingsViewModel(
        IMidiControlSession session,
        IGeneratorPresetRegistry? presets = null,
        ILiveProfileStore? profileStore = null,
        IMappingProfilePortability? portability = null,
        IMappingFilePicker? filePicker = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _profileStore = profileStore;
        _portability = portability;
        _filePicker = filePicker;
        Targets = BuildTargets(presets);
        SelectedTarget = Targets.FirstOrDefault();
        LearnCommand = ReactiveCommand.Create(BeginLearn);
        CancelLearnCommand = ReactiveCommand.Create(CancelLearn);
        RemoveCommand = ReactiveCommand.CreateFromTask(RemoveSelectedAsync);
        ExportMappingCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        ImportMappingCommand = ReactiveCommand.CreateFromTask(ImportAsync);
        _session.MappingChanged += OnMappingChanged;
        Refresh(_session.ActiveProfile);
    }

    public ObservableCollection<MappingTargetViewModel> Targets { get; }
    public ObservableCollection<MappingBindingViewModel> Bindings { get; } = new();

    public MappingTargetViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set => this.RaiseAndSetIfChanged(ref _selectedTarget, value);
    }

    public MappingBindingViewModel? SelectedBinding
    {
        get => _selectedBinding;
        set => this.RaiseAndSetIfChanged(ref _selectedBinding, value);
    }

    public string DeviceName => _session.InputDeviceName ?? "No MIDI input connected";

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public ReactiveCommand<Unit, Unit> LearnCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelLearnCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportMappingCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportMappingCommand { get; }

    private void BeginLearn()
    {
        if (SelectedTarget is null)
            return;

        try
        {
            _session.BeginLearn(
                SelectedTarget.Action,
                SelectedTarget.Slot,
                argument: SelectedTarget.Argument,
                preferredInputMode: SelectedTarget.PreferredInputMode,
                relativeTicksPerRevolution: SelectedTarget.RelativeTicksPerRevolution,
                invert: SelectedTarget.Invert);
            Status = $"Learning {SelectedTarget.Label}: move or press the control now.";
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    private void CancelLearn()
    {
        _session.CancelLearn();
        Status = "MIDI learn cancelled.";
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedBinding is null)
            return;

        await _session.RemoveBindingAsync(SelectedBinding.Binding).ConfigureAwait(false);
    }

    // Export the connected device's current mapping to a user-chosen file, named by its model (doc 05).
    private async Task ExportAsync()
    {
        if (_portability is null || _filePicker is null)
            return;

        ControllerMappingProfile? profile = _session.ActiveProfile;
        if (profile is null)
        {
            Status = "No mapping to export — connect a controller in Settings first.";
            return;
        }

        string? path = await _filePicker.PickExportPathAsync(SuggestedFileName(profile)).ConfigureAwait(false);
        if (path is null)
            return; // cancelled

        try
        {
            await _portability.ExportAsync(profile, path).ConfigureAwait(false);
            Status = $"Exported mapping to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Export failed ({ex.Message}).";
        }
    }

    // Import a mapping file and install it as the connected device's profile (by model). It is saved to the
    // live store under the device name, so the next Save / reconnect in Settings loads and applies it.
    private async Task ImportAsync()
    {
        if (_portability is null || _filePicker is null)
            return;

        string? path = await _filePicker.PickImportPathAsync().ConfigureAwait(false);
        if (path is null)
            return; // cancelled

        ControllerMappingProfile? imported;
        try
        {
            imported = await _portability.ImportAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Import failed ({ex.Message}).";
            return;
        }

        if (imported is null)
        {
            Status = "That file is not a valid Liveolator MIDI map.";
            return;
        }

        string? device = _session.InputDeviceName;
        if (device is null || _profileStore is null)
        {
            Status = $"Imported '{imported.Name}'. Connect that device in Settings, then Save to apply.";
            return;
        }

        // Re-key to the connected device so Settings -> Save (which loads the profile by device name) applies it.
        ControllerMappingProfile installed = imported with { Name = device, DeviceHint = device };
        await _profileStore.SaveMappingProfileAsync(installed).ConfigureAwait(false);
        Status = $"Imported '{imported.Name}' for {device}. Press Save to apply.";
    }

    // A filesystem-safe suggested name based on the device model, e.g. "CMD-Studio-2A-midi-map.json".
    private static string SuggestedFileName(ControllerMappingProfile profile)
    {
        string model = string.IsNullOrWhiteSpace(profile.DeviceHint) ? profile.Name : profile.DeviceHint;
        var slug = new System.Text.StringBuilder(model.Length);
        foreach (char c in model)
            slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        string trimmed = slug.ToString().Trim('-');
        return (trimmed.Length == 0 ? "midi" : trimmed) + "-midi-map.json";
    }

    private void OnMappingChanged(object? sender, ControllerMappingProfile profile)
    {
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            Refresh(profile);
            Status = "Mapping captured, applied, and saved.";
        });
    }

    private void Refresh(ControllerMappingProfile? profile)
    {
        Bindings.Clear();
        if (profile is null)
        {
            Status = "Connect a MIDI controller in Settings.";
            return;
        }

        foreach (ControllerBinding binding in profile.Bindings
                     .OrderBy(binding => binding.Action)
                     .ThenBy(binding => binding.Slot))
            Bindings.Add(new MappingBindingViewModel(binding));

        Status = $"{Bindings.Count} mapping(s) active.";
        this.RaisePropertyChanged(nameof(DeviceName));
    }

    private static ObservableCollection<MappingTargetViewModel> BuildTargets(IGeneratorPresetRegistry? presets)
    {
        var targets = new ObservableCollection<MappingTargetViewModel>(
        [
            new("Deck A: Play / Pause", PerformanceActionKind.DeckPlayPause, 0),
            new("Deck A: Cue", PerformanceActionKind.DeckCue, 0),
            new("Deck A: Sync", PerformanceActionKind.DeckSyncToggle, 0, ActionInputMode.Toggle),
            new("Deck A: Jog / track position", PerformanceActionKind.DeckJog, 0,
                ActionInputMode.Relative, RelativeTicksPerRevolution: 128.0),
            new("Deck A: Channel fader", PerformanceActionKind.MixerChannelGain, 0),
            new("Deck A: EQ High", PerformanceActionKind.MixerEqBand, 0, ActionInputMode.Absolute, Argument: "High"),
            new("Deck A: EQ Mid", PerformanceActionKind.MixerEqBand, 0, ActionInputMode.Absolute, Argument: "Mid"),
            new("Deck A: EQ Low", PerformanceActionKind.MixerEqBand, 0, ActionInputMode.Absolute, Argument: "Low"),
            new("Deck A: Filter", PerformanceActionKind.MixerFilter, 0),
            new("Deck A: Headphone cue", PerformanceActionKind.MixerCueToggle, 0),
            new("Deck B: Play / Pause", PerformanceActionKind.DeckPlayPause, 1),
            new("Deck B: Cue", PerformanceActionKind.DeckCue, 1),
            new("Deck B: Sync", PerformanceActionKind.DeckSyncToggle, 1, ActionInputMode.Toggle),
            new("Deck B: Jog / track position", PerformanceActionKind.DeckJog, 1,
                ActionInputMode.Relative, RelativeTicksPerRevolution: 128.0),
            new("Deck B: Channel fader", PerformanceActionKind.MixerChannelGain, 1),
            new("Deck B: EQ High", PerformanceActionKind.MixerEqBand, 1, ActionInputMode.Absolute, Argument: "High"),
            new("Deck B: EQ Mid", PerformanceActionKind.MixerEqBand, 1, ActionInputMode.Absolute, Argument: "Mid"),
            new("Deck B: EQ Low", PerformanceActionKind.MixerEqBand, 1, ActionInputMode.Absolute, Argument: "Low"),
            new("Deck B: Filter", PerformanceActionKind.MixerFilter, 1),
            new("Deck B: Headphone cue", PerformanceActionKind.MixerCueToggle, 1),
            new("Mixer: Crossfader", PerformanceActionKind.MixerCrossfade, 0),
            new("Beat: Tap tempo", PerformanceActionKind.BeatTapTempo, 0),
            new("Beat: Nudge forward", PerformanceActionKind.BeatNudgeForward, 0),
            new("Beat: Nudge backward", PerformanceActionKind.BeatNudgeBackward, 0),
            new("Visuals: Blackout", PerformanceActionKind.VisualBlackout, 0),
            new("Visuals: Strobe", PerformanceActionKind.VisualToggleStrobe, 0),
        ]);

        // One learn target per controllable parameter of every registered generator preset (doc 28), so a
        // hardware knob can be bound to e.g. GLOW. The binding carries the namespaced macro name as its
        // Argument; the learn session and ControllerBinding already thread Argument through to VisualSetMacro.
        foreach (GeneratorPreset preset in (presets?.Presets ?? Array.Empty<GeneratorPreset>())
                     .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (ControllableParameter parameter in preset.Controllable)
            {
                targets.Add(new MappingTargetViewModel(
                    $"Visuals: {preset.Name} - {parameter.Label}",
                    PerformanceActionKind.VisualSetMacro,
                    Slot: 0,
                    PreferredInputMode: ActionInputMode.Absolute,
                    Argument: GeneratorPresetExpansion.MacroName(preset.PresetId, parameter.Id)));
            }
        }

        return targets;
    }

    public void Dispose() => _session.MappingChanged -= OnMappingChanged;
}
