using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Features.Live;
using Liveolator.App.Shell;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Program Out module (the mock's video preview header): launches the GL visuals window on demand
/// via <see cref="IVisualStage"/> (the render-window seam — never opened during composition). The live
/// preview frame, REC, and per-layer toggles have no backend yet (doc 18) and are shown as a static
/// placeholder so the header matches the mock.
/// </summary>
public sealed class ProgramOutViewModel : ViewModelBase
{
    private readonly IVisualStage? _visualStage;

    public ProgramOutViewModel(IVisualStage? visualStage = null)
    {
        _visualStage = visualStage;
        ShowVisualsCommand = ReactiveCommand.Create(
            () => _visualStage?.Show(), Observable.Return(visualStage is not null));
    }

    /// <summary>True when a visuals window can be launched (drives the "Show Visuals" button).</summary>
    public bool CanShowVisuals => _visualStage is not null;

    public ReactiveCommand<Unit, Unit> ShowVisualsCommand { get; }

    /// <summary>Static program-out summary line (no live signal feed yet).</summary>
    public string ResolutionLabel => "Program Out · 1920×1080 · 60";
}
