namespace Liveolator.App.Features.Live;

/// <summary>
/// Host seam for showing the GL visual compositor window. The compositor's render loop blocks and
/// needs a display, so it cannot run on the UI thread or during composition — this owns launching it
/// on a dedicated thread, once, on demand (the doc 08 "render-window seam").
/// </summary>
public interface IVisualStage
{
    /// <summary>True while the visuals window thread is alive.</summary>
    bool IsShown { get; }

    /// <summary>Launch the visuals window (idempotent — a no-op while already shown).</summary>
    void Show();
}
