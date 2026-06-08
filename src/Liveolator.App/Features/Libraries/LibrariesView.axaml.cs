using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using Liveolator.App.Features.Playlists;

namespace Liveolator.App.Features.Libraries;

public partial class LibrariesView : UserControl
{
    // Single live instance, so repeated "Folders" clicks focus the open window rather than stack.
    private FoldersStatusWindow? _foldersWindow;
    private PlaylistBuilderWindow? _playlistsWindow;

    public LibrariesView() => InitializeComponent();

    // Folder picking is inherently view-bound (needs the TopLevel); the chosen paths are
    // handed to the view-model, which stays UI-free.
    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibrariesViewModel vm)
            return;

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFolder> picked = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Add music folder", AllowMultiple = true });

        foreach (IStorageFolder folder in picked)
        {
            string? path = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                vm.AddFolder(path);
        }
    }

    // Opening a window is a view concern (needs the owner Window), like the folder picker above.
    // The window binds to the live tab view-model, so it reflects scans/adds as they happen.
    private void OnShowFolders(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibrariesViewModel vm)
            return;

        if (_foldersWindow is not null)
        {
            _foldersWindow.Activate();
            return;
        }

        var window = new FoldersStatusWindow { DataContext = vm };
        window.Closed += (_, _) => _foldersWindow = null;
        _foldersWindow = window;

        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    // Opens the playlist/set builder bound to the injected builder view-model. Single-instance,
    // like the Folders window. Initializes (loads the library snapshot + saved sets) on open.
    private void OnShowPlaylists(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibrariesViewModel { PlaylistBuilder: { } builder })
            return;

        if (_playlistsWindow is not null)
        {
            _playlistsWindow.Activate();
            return;
        }

        var window = new PlaylistBuilderWindow { DataContext = builder };
        window.Closed += (_, _) => _playlistsWindow = null;
        _playlistsWindow = window;

        _ = builder.InitializeAsync();

        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    private void OnOpenGetSongBpm(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://getsongbpm.com/")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // The attribution remains visible even when the OS cannot open a browser.
        }
    }
}
