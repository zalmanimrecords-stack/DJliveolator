using Microsoft.Extensions.Logging;

namespace Liveolator.App.Features.Live;

/// <summary>
/// Runs the visual compositor's blocking render loop on a dedicated background thread (the doc 08
/// render-window seam). The actual window loop is injected as a delegate so this launcher — its
/// idempotency, threading, and error isolation — unit-tests without a GL context. A render failure
/// (e.g. no display) is logged and swallowed: it must never take down the app.
/// </summary>
public sealed class VisualStage : IVisualStage
{
    private readonly Action _runWindow;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Thread? _thread;

    public VisualStage(Action runWindow, ILogger logger)
    {
        _runWindow = runWindow ?? throw new ArgumentNullException(nameof(runWindow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsShown
    {
        get { lock (_gate) return _thread is { IsAlive: true }; }
    }

    public void Show()
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
                return;

            _thread = new Thread(RunGuarded)
            {
                IsBackground = true, // never block app shutdown
                Name = "Liveolator GL Visuals",
            };
            _thread.Start();
        }
    }

    private void RunGuarded()
    {
        try
        {
            _logger.LogInformation("Launching visual compositor window.");
            _runWindow();
        }
        catch (Exception ex)
        {
            // No display, GL init failure, etc. — surface it but keep the app alive (doc 08; standards #16/#26).
            _logger.LogError(ex, "Visual compositor window exited with an error.");
        }
    }
}
