using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Actions;

/// <summary>
/// Calls the handlers' time-driven work on a background thread at a fixed interval: the jog-release poll
/// (an endless jog encoder sends no "release", so <c>DeckActionHandler.PumpJogRelease</c> must be polled to
/// snap a stale bend back) and the AUTO crossfade step (<c>MixerActionHandler.PumpAutoCrossfade</c>). Kept off
/// the UI thread — a stall must not leave a deck detuned or a fade frozen mid-mix — and mirrors
/// <c>MasterClockPump</c>. A tick that throws is logged and the loop continues.
/// </summary>
public sealed class HandlerPump : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(30);

    private readonly Action _tick;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();

    private Thread? _thread;
    private bool _disposed;

    public HandlerPump(Action tick, TimeSpan? interval = null, ILogger<HandlerPump>? logger = null)
    {
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), _interval, "Pump interval must be positive.");
        _logger = logger ?? NullLogger<HandlerPump>.Instance;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _thread is { IsAlive: true };
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is { IsAlive: true })
                return;

            _thread = new Thread(Run) { IsBackground = true, Name = "Liveolator Handler Pump" };
            _thread.Start();
        }
    }

    private void Run()
    {
        try
        {
            while (!_stop.IsSet)
            {
                try
                {
                    _tick();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Handler pump tick failed; continuing.");
                }

                _stop.Wait(_interval);
            }
        }
        finally
        {
            lock (_gate)
                _thread = null;
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _stop.Set();
            thread = _thread;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));

        _stop.Dispose();
    }
}
