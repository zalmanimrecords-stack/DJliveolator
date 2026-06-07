using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Beat;

/// <summary>
/// Pumps the sync correction loop and deck-driven shared clock on a dedicated background thread, so
/// UI stalls cannot interrupt phase lock. The pump owns no clock state; it only timestamps and calls
/// the existing <see cref="MasterClockBridge"/>.
/// </summary>
public sealed class MasterClockPump : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(10);

    private readonly MasterClockBridge _bridge;
    private readonly IHostClock _hostClock;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();

    private Thread? _thread;
    private bool _disposed;

    public MasterClockPump(
        MasterClockBridge bridge,
        IHostClock hostClock,
        TimeSpan? interval = null,
        ILogger<MasterClockPump>? logger = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _hostClock = hostClock ?? throw new ArgumentNullException(nameof(hostClock));
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), _interval, "Pump interval must be positive.");
        _logger = logger ?? NullLogger<MasterClockPump>.Instance;
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

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Liveolator Sync Clock",
            };
            _thread.Start();
        }
    }

    private void Run()
    {
        try
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not raise the sync-clock thread priority.");
            }

            while (!_stop.IsSet)
            {
                try
                {
                    _bridge.Tick(_hostClock.NowTicks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync-clock pump tick failed; continuing.");
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
