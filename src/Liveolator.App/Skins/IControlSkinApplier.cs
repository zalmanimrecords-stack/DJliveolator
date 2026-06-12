using Avalonia;
using Avalonia.Threading;
using Liveolator.Core.Skins;

namespace Liveolator.App.Skins;

/// <summary>
/// Applies the active control skins to the live UI (doc 30). A seam so the Avalonia-free
/// <c>SettingsViewModel</c> can re-skin on Save without depending on <see cref="Application"/> directly
/// (mirroring how it applies audio/deck changes through other seams). Pass <c>null</c> for a control to
/// reset it to the themed built-in look.
/// </summary>
public interface IControlSkinApplier
{
    void Apply(ControlSkinFile? knob, ControlSkinFile? slider);
}

/// <summary>Default <see cref="IControlSkinApplier"/>: re-skins the running <see cref="Application"/> via <see cref="ControlSkinApplier"/>.</summary>
public sealed class ApplicationControlSkinApplier : IControlSkinApplier
{
    public void Apply(ControlSkinFile? knob, ControlSkinFile? slider)
    {
        if (Application.Current is not { } app)
            return;

        // SaveAsync runs on a ReactiveUI background thread; mutating Application.Resources (and the brushes it
        // holds) is UI-thread-affine, so marshal there. Invoke runs inline when already on the UI thread.
        Dispatcher.UIThread.Invoke(() => ControlSkinApplier.Apply(app, knob, slider));
    }
}
