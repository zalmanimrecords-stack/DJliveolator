using Microsoft.Extensions.Logging;

namespace Liveolator.App.Features.Live;

/// <summary>
/// Runs the visual compositor's blocking render loop on a dedicated background thread (the doc 08
/// render-window seam). The window loop and the "reveal" action are injected as delegates so this
/// launcher — its idempotency, threading, and error isolation — unit-tests without a GL context.
///
/// The loop can start <b>hidden</b> (<see cref="Start"/>) so the in-app preview is live from launch
/// without popping an output window; <see cref="Show"/> then reveals that running loop (or starts it
/// visible if nothing is running yet). A render failure (e.g. no display) is logged and swallowed: it
/// must never take down the app.
/// </summary>
public sealed class VisualStage : IVisualStage
{
    // Runs the blocking window loop; the bool is the initial visibility.
    private readonly Action<bool> _runWindow;
    // Reveals an already-running hidden window (thread-safe; no-op if already visible / not running).
    private readonly Action _present;
    // Signals the running window loop to close on its next frame (thread-safe; no-op if not running).
    private readonly Action _stop;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Thread? _thread;

    public VisualStage(Action<bool> runWindow, Action present, ILogger logger, Action? stop = null)
    {
        _runWindow = runWindow ?? throw new ArgumentNullException(nameof(runWindow));
        _present = present ?? throw new ArgumentNullException(nameof(present));
        // No stop delegate => Stop() can only wait the thread out; production always supplies one.
        _stop = stop ?? (() => { });
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsShown
    {
        get { lock (_gate) return _thread is { IsAlive: true }; }
    }

    public void Start() => EnsureRunning(visible: false);

    public void Show() => EnsureRunning(visible: true);

    public void Stop(TimeSpan timeout)
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is not { IsAlive: true })
                return;
        }

        _logger.LogInformation("Stopping the visual compositor render loop.");
        try
        {
            // Ask the loop to close its window on its own thread (touching the GL window off-thread is
            // unsafe); the signal is a thread-safe flag the render callback observes.
            _stop();
        }
        catch (Exception ex)
        {
            // The loop is a background thread, so even a failed signal can never block app shutdown
            // (standards #16/#26) — the join below times out and the thread is abandoned.
            _logger.LogError(ex, "Signalling the visual render loop to stop failed.");
        }

        if (!thread.Join(timeout))
            _logger.LogWarning(
                "Visual render thread did not exit within {TimeoutMs} ms; abandoning it (it is a background thread).",
                timeout.TotalMilliseconds);
    }

    // Starts the loop if it is not running. If it is already running and a visible loop is requested,
    // reveals the existing (possibly hidden) window instead of launching a second one.
    private void EnsureRunning(bool visible)
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
            {
                if (visible)
                    _present();
                return;
            }

            _thread = new Thread(() => RunGuarded(visible))
            {
                IsBackground = true, // never block app shutdown
                Name = "Liveolator GL Visuals",
            };
            _thread.Start();
        }
    }

    private void RunGuarded(bool visible)
    {
        try
        {
            _logger.LogInformation("Launching visual compositor render loop (visible={Visible}).", visible);
            _runWindow(visible);
        }
        catch (Exception ex)
        {
            // No display, GL init failure, etc. — surface it but keep the app alive (doc 08; standards #16/#26).
            _logger.LogError(ex, "Visual compositor window exited with an error.");
        }
    }
}
