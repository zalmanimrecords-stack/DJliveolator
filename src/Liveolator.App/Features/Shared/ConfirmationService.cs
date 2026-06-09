using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Liveolator.App.Features.Shared;

/// <summary>Avalonia <see cref="IConfirmationService"/>: a small modal window with confirm / cancel
/// buttons, shown over the main window (mirrors the <see cref="TrackEditor"/> dialog pattern).</summary>
public sealed class ConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "OK")
    {
        var window = new ConfirmationWindow();
        window.Configure(title, message, confirmLabel);
        Window? owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return owner is null
            ? ShowWithoutOwner(window)
            : window.ShowDialog<bool>(owner);
    }

    private static async Task<bool> ShowWithoutOwner(ConfirmationWindow window)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => completion.TrySetResult(false);
        window.Show();
        return await completion.Task.ConfigureAwait(false);
    }
}
