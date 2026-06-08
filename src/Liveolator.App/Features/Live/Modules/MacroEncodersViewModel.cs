using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Push encoder row (doc 12 / the mock's "Push Encoders · visual macros"): eight ring controls
/// bound to visual macros (intensity / speed / echo / particles / kaleido / zoom / hue / opacity, doc 08).
/// Each encoder emits a <see cref="PerformanceActionKind.VisualSetMacro"/> with the macro name in
/// <c>Argument</c> and the normalized value (doc 04) — the same action a physical Push encoder produces,
/// so the on-screen row and the hardware stay in lockstep. Macros the engine has not registered yet are
/// no-ops on the engine side (logged), but the action layer is fully wired here.
/// </summary>
public sealed class MacroEncodersViewModel : ViewModelBase, IDisposable
{
    // Label → macro name (the Argument the VisualActionHandler forwards to the engine), with the
    // mock's starting positions for visual fidelity.
    private static readonly (string Label, string Macro, double Initial)[] Specs =
    {
        ("Intensity", "intensity", 0.78),
        ("Speed", "speed", 0.42),
        ("Echo", "echo", 0.30),
        ("Particles", "particles", 0.55),
        ("Kaleido", "kaleido", 0.61),
        ("Zoom", "zoom", 0.50),
        ("Hue", "hue", 0.85),
        ("Opacity", "opacity", 1.00),
    };

    private readonly IPerformanceActionDispatcher? _dispatcher;

    public MacroEncodersViewModel(IPerformanceActionDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
        bool enabled = dispatcher is not null;

        Encoders = new ObservableCollection<ContinuousControlViewModel>(
            Specs.Select(spec => new ContinuousControlViewModel(
                spec.Label, spec.Initial,
                enabled ? value => Emit(spec.Macro, value) : null)));

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>True when the visual handler is wired; the UI disables the encoders otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    /// <summary>The eight macro ring encoders, in display order.</summary>
    public ObservableCollection<ContinuousControlViewModel> Encoders { get; }

    private void Emit(string macro, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.VisualSetMacro, ActionInputMode.Absolute, Value: value, Argument: macro));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind != PerformanceActionKind.VisualSetMacro || string.IsNullOrWhiteSpace(e.State.Argument))
            return;

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            int index = Array.FindIndex(
                Specs,
                spec => string.Equals(spec.Macro, e.State.Argument, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                Encoders[index].SetFromFeedback(e.State.Value);
        });
    }

    public void Dispose()
    {
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }
}
