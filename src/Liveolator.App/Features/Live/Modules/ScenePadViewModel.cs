using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// One cell of the Visual Scene Grid (doc 12 Module 4), mirroring a Push pad. Triggering it emits a
/// <see cref="PerformanceActionKind.VisualLoadScene"/> for its slot through the dispatcher (doc 04).
/// Its visual state — empty / loaded / active — is driven entirely by dispatcher feedback so the
/// on-screen grid and the Push pads share one source of truth (doc 12), never independent local state.
/// </summary>
public sealed class ScenePadViewModel : ViewModelBase
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private bool _isLoaded;
    private bool _isActive;

    public ScenePadViewModel(int slot, IPerformanceActionDispatcher? dispatcher = null)
    {
        Slot = slot;
        _dispatcher = dispatcher;
        LaunchCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.VisualLoadScene, Slot: slot)),
            Observable.Return(dispatcher is not null));
    }

    /// <summary>The scene slot this pad addresses (0-based, row-major across the 8×8 grid).</summary>
    public int Slot { get; }

    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }

    /// <summary>True when the active bank has a scene in this slot (a "loaded" pad vs an empty cell).</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => this.RaiseAndSetIfChanged(ref _isLoaded, value);
    }

    /// <summary>True when this is the currently active scene (accent-filled pad).</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>Applies a feedback snapshot: availability → loaded, active → the lit pad.</summary>
    public void Apply(ActionFeedbackState state)
    {
        IsLoaded = state.IsAvailable;
        IsActive = state.IsActive;
    }
}
