using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Liveolator.Core.Update;

namespace Liveolator.App.Features.Update;

/// <summary>
/// Avalonia <see cref="IUpdatePrompt"/>: shows <see cref="UpdateAvailableWindow"/> as a modal over the
/// main window (mirrors <c>ConfirmationService</c>). Marshals onto the UI thread itself, so the caller
/// may invoke it from the background continuation of a network fetch. A missing owner (no main window
/// yet) resolves to <see cref="UpdateDialogChoice.Later"/> rather than blocking.
/// </summary>
public sealed class AvaloniaUpdatePrompt : IUpdatePrompt
{
    public Task<UpdateDialogChoice> PromptAsync(UpdateManifest manifest, string currentVersion)
    {
        // Hop to the UI thread to build/show the window, then bridge the dialog's Task back to the caller.
        // A TaskCompletionSource keeps the marshaling explicit and free of InvokeAsync overload ambiguity.
        var completion = new TaskCompletionSource<UpdateDialogChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Show(manifest, currentVersion).ContinueWith(
                    t => Forward(t, completion), TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private static void Forward(Task<UpdateDialogChoice> dialog, TaskCompletionSource<UpdateDialogChoice> completion)
    {
        if (dialog.IsFaulted)
            completion.TrySetException(dialog.Exception!.GetBaseException());
        else if (dialog.IsCanceled)
            completion.TrySetResult(UpdateDialogChoice.Later);
        else
            completion.TrySetResult(dialog.Result);
    }

    private static Task<UpdateDialogChoice> Show(UpdateManifest manifest, string currentVersion)
    {
        var window = new UpdateAvailableWindow();
        window.Configure(manifest, currentVersion);
        Window? owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return owner is null
            ? Task.FromResult(UpdateDialogChoice.Later)
            : window.ShowDialog<UpdateDialogChoice>(owner);
    }
}
