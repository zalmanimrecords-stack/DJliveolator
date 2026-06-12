using Avalonia;
using Avalonia.Threading;
using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

/// <summary>
/// Applies a UI theme to the live application without a restart (the Settings "Apply" button). A seam so the
/// Avalonia-free <c>SettingsViewModel</c> can re-theme without touching <see cref="Application"/> directly.
/// </summary>
public interface IUiThemeLiveApplier
{
    void Apply(UiThemeDefinition theme);
}

/// <summary>
/// Default <see cref="IUiThemeLiveApplier"/>: re-themes the running <see cref="Application"/> via
/// <see cref="UiThemeApplier"/>. Marshals to the UI thread — Settings actions can run on a ReactiveUI
/// background thread, and theme application mutates <c>Application.Resources</c>/brushes, which is
/// UI-thread-affine (the same crash class fixed for control skins). Invoke runs inline when already on it.
/// </summary>
public sealed class ApplicationUiThemeLiveApplier : IUiThemeLiveApplier
{
    public void Apply(UiThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (Application.Current is not { } app)
            return;
        Dispatcher.UIThread.Invoke(() => UiThemeApplier.Apply(app, theme));
    }
}
