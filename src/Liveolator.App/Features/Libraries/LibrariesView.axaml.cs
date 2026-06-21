using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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

    // Importing another DJ app's library is view-bound (the file picker needs the TopLevel); the chosen
    // file path is handed to the view-model, which does the format-agnostic parse + merge UI-free.
    private async void OnImportRekordbox(object? sender, RoutedEventArgs e)
        => await ImportLibraryAsync("Rekordbox", "Rekordbox collection (XML)", "*.xml");

    private async void OnImportTraktor(object? sender, RoutedEventArgs e)
        => await ImportLibraryAsync("Traktor", "Traktor collection (NML)", "*.nml");

    private async Task ImportLibraryAsync(string format, string description, string pattern)
    {
        if (DataContext is not LibrariesViewModel vm)
            return;

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = $"Import {format} library",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(description) { Patterns = new[] { pattern } },
                },
            });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await vm.ImportFromFileAsync(format, path);
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
