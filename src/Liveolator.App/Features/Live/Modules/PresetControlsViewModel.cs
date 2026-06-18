using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The controllable-preset knob surface for a single compositor layer (doc 28). When that layer's source
/// is set to a generator backed by a registered <see cref="GeneratorPreset"/>, this surface loads the
/// preset onto the layer (<see cref="PerformanceActionKind.VisualLoadPreset"/> — the engine installs the
/// macros and places the generator) and exposes up to five labelled knobs, one per
/// <see cref="ControllableParameter"/>. Each knob emits <see cref="PerformanceActionKind.VisualSetMacro"/>
/// with the preset's namespaced macro name — exactly the action a learned MIDI knob produces, so the
/// on-screen knob and the controller stay in lockstep. A non-preset source clears the knobs.
/// </summary>
public sealed class PresetControlsViewModel : ViewModelBase, IDisposable
{
    private readonly IGeneratorPresetRegistry? _presets;
    private readonly IVisualEffectRegistry? _effects;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly int _targetLayer;
    private readonly Dictionary<string, ContinuousControlViewModel> _controlsByMacro = new(StringComparer.Ordinal);
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

        Controls = new ObservableCollection<ContinuousControlViewModel>();

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>True when presets + effects + dispatcher are all wired; the surface never loads otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null && _presets is not null && _effects is not null;

    /// <summary>The compositor layer a loaded preset occupies.</summary>
    public int TargetLayer => _targetLayer;

    /// <summary>The active preset's controllable knobs (≤5), rebuilt on each load and cleared otherwise.</summary>
    public ObservableCollection<ContinuousControlViewModel> Controls { get; }

    public bool HasControls => Controls.Count > 0;

    /// <summary>The preset id currently loaded onto the layer, or null when no preset is loaded.</summary>
    public string? ActivePresetId => _activePresetId;

    /// <summary>
    /// Loads the controllable preset that wraps <paramref name="generatorEffectId"/> (if one is registered)
    /// onto the target layer, building its knob row. Returns true when a preset was loaded; false (and the
    /// knobs are cleared) when the id is null/unknown, no preset wraps it, or the surface is unwired. This is
    /// the single entry point used when a layer's source dropdown selects a generator.
    /// </summary>
    public bool TryLoadForGeneratorSource(string? generatorEffectId)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(generatorEffectId))
        {
            ClearControls();
            return false;
        }

        GeneratorPreset? match = _presets!.Presets.FirstOrDefault(
            preset => string.Equals(preset.GeneratorEffectId, generatorEffectId, StringComparison.Ordinal));
        if (match is null)
        {
            ClearControls();
            return false;
        }

        LoadPreset(match.PresetId);
        return _activePresetId is not null;
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

    /// <summary>Clears the knob row (used when the layer's source is not a controllable preset).</summary>
    public void ClearControls()
    {
        if (_activePresetId is null && Controls.Count == 0)
            return;
        _controlsByMacro.Clear();
        Controls.Clear();
        _activePresetId = null;
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
