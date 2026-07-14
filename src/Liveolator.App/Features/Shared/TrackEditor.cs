using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Shared;

public sealed class TrackEditor : ITrackEditor
{
    public Task<TrackEditResult?> EditAsync(MusicTrack track)
    {
        var window = new TrackEditorWindow();
        window.Load(track);
        Window? owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return owner is null
            ? ShowWithoutOwner(window)
            : window.ShowDialog<TrackEditResult?>(owner);
    }

    private static async Task<TrackEditResult?> ShowWithoutOwner(TrackEditorWindow window)
    {
        var completion = new TaskCompletionSource<TrackEditResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => completion.TrySetResult(null);
        window.Show();
        return await completion.Task.ConfigureAwait(false);
    }
}
