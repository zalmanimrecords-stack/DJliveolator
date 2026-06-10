namespace Liveolator.App.Features.Live;

/// <summary>
/// Host seam for showing the GL visual compositor window. The compositor's render loop blocks and
/// needs a display, so it cannot run on the UI thread or during composition — this owns launching it
/// on a dedicated thread, once, on demand (the doc 08 "render-window seam").
/// </summary>
public interface IVisualStage
{
    /// <summary>True while the visuals render-loop thread is alive (hidden or shown).</summary>
    bool IsShown { get; }

    /// <summary>
    /// Start the render loop hidden (idempotent). Feeds the in-app preview without opening the output
    /// window; call at app startup so the Program Out monitor is live from launch.
    /// </summary>
    void Start();

    /// <summary>
    /// Reveal the output window (idempotent). Reveals the already-running hidden loop, or starts it
    /// visible if it is not running yet.
    /// </summary>
    void Show();
}
