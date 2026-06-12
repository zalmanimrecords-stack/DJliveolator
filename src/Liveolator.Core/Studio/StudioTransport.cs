using System.Threading;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;

namespace Liveolator.Core.Studio;

/// <summary>
/// Plays a STUDIO arrangement live by driving the real decks: a host-clock ticker advances a
/// playhead and, through <see cref="StudioArranger"/>, dispatches the due clip transport + automation
/// <see cref="PerformanceAction"/>s to the engine via the one dispatcher (Origin = "studio"). Pure
/// managed (clock + dispatcher seams), so the scheduling step <see cref="Advance"/> unit-tests with a
/// fake dispatcher and no audio. Threading mirrors <see cref="MasterClockPump"/>.
/// </summary>
public sealed class StudioTransport : IDisposable
{
    private static readonly TimeSpan DefaultTick = TimeSpan.FromMilliseconds(10);

    private readonly StudioArranger _arranger;
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly IHostClock _clock;
    private readonly TimeSpan _tick;
    private readonly object _gate = new();

    private Thread? _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private long _anchorTicks;          // host ticks at which the current play span started
    private double _anchorSeconds;      // playhead position at that anchor
    private double _positionSeconds;    // last advanced playhead
    private double _dispatchedThrough;  // upper bound (exclusive) of clip events already emitted
    private bool _running;
    private bool _disposed;

    public StudioTransport(
        StudioArranger arranger,
        IPerformanceActionDispatcher dispatcher,
        IHostClock clock,
        TimeSpan? tickInterval = null)
    {
        _arranger = arranger ?? throw new ArgumentNullException(nameof(arranger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _tick = tickInterval ?? DefaultTick;
    }

    /// <summary>The current playhead position in timeline seconds.</summary>
    public double PositionSeconds { get { lock (_gate) return _positionSeconds; } }

    /// <summary>True while the transport ticker is running.</summary>
    public bool IsPlaying { get { lock (_gate) return _running; } }

    /// <summary>Start (or resume) playback from the current playhead.</summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_disposed || _running)
                return;
            _anchorSeconds = _positionSeconds;
            _anchorTicks = _clock.NowTicks;
            _running = true;
            _stop.Reset();
            _thread = new Thread(Run) { IsBackground = true, Name = "StudioTransport" };
            _thread.Start();
        }
    }

    /// <summary>Pause playback, holding the playhead in place.</summary>
    public void Pause()
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_running)
                return;
            _running = false;
            _stop.Set();
            thread = _thread;
            _thread = null;
        }
        thread?.Join(TimeSpan.FromMilliseconds(200));
    }

    /// <summary>Stop playback and return the playhead to the start.</summary>
    public void Stop()
    {
        Pause();
        Seek(0);
    }

    /// <summary>
    /// Move the playhead to <paramref name="seconds"/> without playing. Clip events at or after the new
    /// position will fire on the next advance; earlier ones are considered already handled.
    /// </summary>
    public void Seek(double seconds)
    {
        double clamped = Math.Max(0, seconds);
        lock (_gate)
        {
            _positionSeconds = clamped;
            _anchorSeconds = clamped;
            _anchorTicks = _clock.NowTicks;
            _dispatchedThrough = clamped;
        }
    }

    /// <summary>
    /// Advance the playhead to <paramref name="toSeconds"/>, dispatching every clip Start/Stop whose
    /// time lies in the half-open window since the last advance, then the automation values at the new
    /// position. Public and side-effect-only-through-the-dispatcher, so it unit-tests deterministically.
    /// </summary>
    public void Advance(double toSeconds)
    {
        double from;
        lock (_gate)
        {
            from = _dispatchedThrough;
            _positionSeconds = toSeconds;
            _dispatchedThrough = Math.Max(_dispatchedThrough, toSeconds);
        }

        if (toSeconds > from)
        {
            foreach (StudioClipEvent ev in _arranger.ClipEventsBetween(from, toSeconds))
                DispatchClipEvent(ev);
        }

        foreach (PerformanceAction action in _arranger.AutomationActionsAt(toSeconds))
            _dispatcher.Dispatch(action);
    }

    private void DispatchClipEvent(StudioClipEvent ev)
    {
        int slot = ev.Clip.DeckSlot;
        if (ev.Kind == StudioClipEventKind.Start)
        {
            // Load then start. Value carries the deck base BPM (0 = native rate); the arrangement
            // positions clips in time, it does not beatmatch, so the native rate is the MVP behaviour.
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckLoadTrack, ActionInputMode.Momentary,
                Value: 0, Slot: slot, Argument: ev.Clip.TrackPath, Origin: StudioArranger.Origin));
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckPlayPause, Slot: slot, Origin: StudioArranger.Origin));
        }
        else
        {
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.TransportStop, Slot: slot, Origin: StudioArranger.Origin));
        }
    }

    private void Run()
    {
        while (!_stop.IsSet)
        {
            long now;
            double anchorSeconds, anchorTicks;
            lock (_gate)
            {
                if (!_running)
                    break;
                now = _clock.NowTicks;
                anchorSeconds = _anchorSeconds;
                anchorTicks = _anchorTicks;
            }

            double elapsed = (now - anchorTicks) / (double)_clock.TicksPerSecond;
            Advance(anchorSeconds + Math.Max(0, elapsed));
            _stop.Wait(_tick);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Pause();
        _stop.Dispose();
    }
}
