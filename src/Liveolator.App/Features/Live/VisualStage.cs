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
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Thread? _thread;

    public VisualStage(Action<bool> runWindow, Action present, ILogger logger)
    {
        _runWindow = runWindow ?? throw new ArgumentNullException(nameof(runWindow));
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsShown
    {
        get { lock (_gate) return _thread is { IsAlive: true }; }
    }

    public void Start() => EnsureRunning(visible: false);

    public void Show() => EnsureRunning(visible: true);

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
