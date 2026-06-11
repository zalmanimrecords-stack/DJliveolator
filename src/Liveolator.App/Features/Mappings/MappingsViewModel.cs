using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Mappings;

public sealed class MappingsViewModel : ViewModelBase, IDisposable
{
    private readonly IMidiControlSession _session;
    private MappingTargetViewModel? _selectedTarget;
    private MappingBindingViewModel? _selectedBinding;
    private string _status = string.Empty;

    public MappingsViewModel(IMidiControlSession session, IGeneratorPresetRegistry? presets = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Targets = BuildTargets(presets);
        SelectedTarget = Targets.FirstOrDefault();
        LearnCommand = ReactiveCommand.Create(BeginLearn);
        CancelLearnCommand = ReactiveCommand.Create(CancelLearn);
        RemoveCommand = ReactiveCommand.CreateFromTask(RemoveSelectedAsync);
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
