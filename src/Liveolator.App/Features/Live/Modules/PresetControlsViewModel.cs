using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The controllable-preset surface (doc 28): a picker of the registered <see cref="GeneratorPreset"/>s
/// plus, for the active preset, up to five labelled knobs — one per <see cref="ControllableParameter"/>.
/// Selecting a preset dispatches <see cref="PerformanceActionKind.VisualLoadPreset"/> (the engine installs
/// the macros + places the generator); each knob then emits <see cref="PerformanceActionKind.VisualSetMacro"/>
/// with the preset's namespaced macro name — exactly the action a learned MIDI knob produces, so the
/// on-screen knob and the controller stay in lockstep. With the registries/dispatcher unwired the surface
/// is disabled and never emits.
/// </summary>
public sealed class PresetControlsViewModel : ViewModelBase, IDisposable
{
    private readonly IGeneratorPresetRegistry? _presets;
    private readonly IVisualEffectRegistry? _effects;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly int _targetLayer;
    private readonly Dictionary<string, ContinuousControlViewModel> _controlsByMacro = new(StringComparer.Ordinal);
    private PresetOptionViewModel? _selectedPreset;
    private string? _activePresetId;

    public PresetControlsViewModel(
        IGeneratorPresetRegistry? presets = null,
        IVisualEffectRegistry? effects = null,
        IPerformanceActionDispatcher? dispatcher = null,
        int targetLayer = 0)
    {
        _presets = presets;
        _effects = effects;
        _dispatcher = dispatcher;
        _targetLayer = targetLayer;

        Presets = new ObservableCollection<PresetOptionViewModel>(
            (presets?.Presets ?? Array.Empty<GeneratorPreset>())
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(preset => new PresetOptionViewModel(preset.PresetId, preset.Name)));
        Controls = new ObservableCollection<ContinuousControlViewModel>();

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>True when presets + effects + dispatcher are all wired; the UI disables the surface otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null && _presets is not null && _effects is not null;

    /// <summary>The compositor layer a loaded preset occupies (doc 28: one dedicated layer).</summary>
    public int TargetLayer => _targetLayer;

    /// <summary>The registered presets, ordered by name, for the picker.</summary>
    public ObservableCollection<PresetOptionViewModel> Presets { get; }

    /// <summary>The active preset's controllable knobs (≤5), rebuilt on each load.</summary>
    public ObservableCollection<ContinuousControlViewModel> Controls { get; }

    public bool HasControls => Controls.Count > 0;

    /// <summary>The preset id currently loaded, or null before any load.</summary>
    public string? ActivePresetId => _activePresetId;

    /// <summary>The picker selection; setting it to a preset loads that preset.</summary>
    public PresetOptionViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPreset, value);
            if (value is not null)
                LoadPreset(value.PresetId);
        }
    }

    /// <summary>
    /// Loads a preset onto the target layer: dispatches <c>VisualLoadPreset</c> and rebuilds the knob row
    /// for the preset's controllable parameters, seeded to the descriptor defaults. A no-op when the
    /// surface is unwired, the preset/generator is unknown, or the preset cannot expand.
    /// </summary>
    public void LoadPreset(string presetId)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(presetId))
            return;
        if (!_presets!.TryGet(presetId, out GeneratorPreset preset))
            return;
        if (!_effects!.TryGet(preset.GeneratorEffectId, preset.GeneratorVersion, out VisualEffectDescriptor descriptor))
            return;

        GeneratorPresetBinding binding;
        try
        {
            binding = GeneratorPresetExpansion.Expand(preset, descriptor, _targetLayer);
        }
        catch (ArgumentException)
        {
            return;
        }

        _dispatcher!.Dispatch(new PerformanceAction(
            PerformanceActionKind.VisualLoadPreset, Slot: _targetLayer, Argument: presetId));

        _activePresetId = presetId;
        _controlsByMacro.Clear();
        Controls.Clear();
        foreach (ControllableParameter parameter in preset.Controllable)
        {
            string macro = GeneratorPresetExpansion.MacroName(presetId, parameter.Id);
            double initial = binding.InitialMacroValues.TryGetValue(macro, out double seeded) ? seeded : 0.5;
            var control = new ContinuousControlViewModel(parameter.Label, initial, value => EmitMacro(macro, value));
            _controlsByMacro[macro] = control;
            Controls.Add(control);
        }

        this.RaisePropertyChanged(nameof(HasControls));
        this.RaisePropertyChanged(nameof(ActivePresetId));
    }

    private void EmitMacro(string macro, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.VisualSetMacro, ActionInputMode.Absolute, Value: value, Argument: macro));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind != PerformanceActionKind.VisualSetMacro || string.IsNullOrWhiteSpace(e.State.Argument))
            return;

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            if (_controlsByMacro.TryGetValue(e.State.Argument!, out ContinuousControlViewModel? control))
                control.SetFromFeedback(e.State.Value);
        });
    }

    public void Dispose()
    {
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }
}
