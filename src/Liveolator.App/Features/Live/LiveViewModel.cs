using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;

namespace Liveolator.App.Features.Live;

/// <summary>
/// The Live performance tab — a composition root over the performance modules that mirror the mock
/// (<c>design/mockups/live-mode-clean.html</c>): Program Out, Beat Engine, Deck A / Mixer / Deck B,
/// Visual Scene Grid, Master/FX and the Push encoder macros. Each module is its own view-model and
/// drives the engines only through the one <see cref="IPerformanceActionDispatcher"/> (doc 04 — the UI
/// is just another action source). This parent owns the render-loop timer that pumps the shared
/// <see cref="ManualBeatClock"/> so the beat phase/pulse advance smoothly between taps, and disposes the
/// child modules. Best-effort like the Libraries tab: with no dispatcher the modules disable themselves
/// and the app still launches.
/// </summary>
public sealed class LiveViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IManualBeatClockDriver? _clockDriver;
    private readonly IHostClock? _hostClock;
    private readonly ILiveBeatTimer? _timer;
    private bool _disposed;

    /// <param name="dispatcher">The one action layer for all module intent; null disables every control.</param>
    /// <param name="beatClock">Shared clock the beat readout follows; null leaves it idle.</param>
    /// <param name="clockDriver">The manual clock's render-loop pump; null disables smooth advance.</param>
    /// <param name="hostClock">Monotonic host time used to stamp each pump tick.</param>
    /// <param name="timer">Render-loop seam driving <paramref name="clockDriver"/>; null disables it.</param>
    /// <param name="visualStage">Launches the GL visuals window on demand; null hides the control.</param>
    public LiveViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IBeatClock? beatClock = null,
        IManualBeatClockDriver? clockDriver = null,
        IHostClock? hostClock = null,
        ILiveBeatTimer? timer = null,
        IVisualStage? visualStage = null)
    {
        _dispatcher = dispatcher;
        _clockDriver = clockDriver;
        _hostClock = hostClock;
        _timer = timer;

        ProgramOut = new ProgramOutViewModel(visualStage);
        Beat = new BeatEngineViewModel(dispatcher, beatClock);
        DeckA = new DeckViewModel(slot: 0, dispatcher);
        DeckB = new DeckViewModel(slot: 1, dispatcher);
        Mixer = new MixerViewModel(dispatcher);
        SceneGrid = new SceneGridViewModel(dispatcher);
        MasterFx = new MasterFxViewModel(dispatcher);
        MacroEncoders = new MacroEncodersViewModel(dispatcher);

        if (_timer is not null && _clockDriver is not null && _hostClock is not null)
        {
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    /// <summary>True when intent can be emitted (the action layer is wired). The view disables the footer hint otherwise.</summary>
    public bool IsLiveModeEnabled => _dispatcher is not null;

    public ProgramOutViewModel ProgramOut { get; }
    public BeatEngineViewModel Beat { get; }
    public DeckViewModel DeckA { get; }
    public DeckViewModel DeckB { get; }
    public MixerViewModel Mixer { get; }
    public SceneGridViewModel SceneGrid { get; }
    public MasterFxViewModel MasterFx { get; }
    public MacroEncodersViewModel MacroEncoders { get; }

    /// <summary>Stops the render loop and disposes the modules — call when the tab/window closes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_timer is not null)
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
        }

        Beat.Dispose();
        DeckA.Dispose();
        DeckB.Dispose();
        Mixer.Dispose();
        SceneGrid.Dispose();
        MasterFx.Dispose();
    }

    // The render loop pumps the manual clock so phase + the pulse advance smoothly between taps.
    private void OnTimerTick(object? sender, EventArgs e)
        => _clockDriver?.Update(_hostClock!.NowTicks);
}
