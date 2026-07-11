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

    // Double-click a track to load it onto Deck A (the standard DJ-browser gesture). The first click
    // selects the row, so the second acts on the now-selected track. No-op when deck A isn't backed.
    private void OnTrackDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibrariesViewModel vm)
            vm.LoadSelectedToDeckA();
    }

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
            // TryGetLocalPath returns null for some network/virtual picks; fall back to the file URI
            // (covers UNC \\server\share paths). If still unresolved, surface it instead of dropping
            // the folder silently (global #26) — a network folder that "won't add" was the symptom.
            string? path = folder.TryGetLocalPath()
                ?? (folder.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null);
            if (!string.IsNullOrEmpty(path))
                vm.AddFolder(path);
            else
                vm.ReportFolderUnavailable(folder.Name);
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

    private async void OnImportVirtualDj(object? sender, RoutedEventArgs e)
        => await ImportLibraryAsync("VirtualDJ", "VirtualDJ database (XML)", "*.xml");

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

    // Folder-based imports: the DJ picks a folder. Serato = the library/drive root (per-file cues/grids +
    // _Serato_ crates); Mixxx = the folder holding mixxxdb.sqlite. The view-model does the format-agnostic
    // parse + merge UI-free.
    private async void OnImportSerato(object? sender, RoutedEventArgs e)
        => await ImportFolderLibraryAsync("Serato", "Import Serato library — pick the library/drive root");

    private async void OnImportMixxx(object? sender, RoutedEventArgs e)
        => await ImportFolderLibraryAsync("Mixxx", "Import Mixxx library — pick the folder holding mixxxdb.sqlite");

    private async void OnImportEngine(object? sender, RoutedEventArgs e)
        => await ImportFolderLibraryAsync("Engine DJ", "Import Engine DJ library — pick the Engine Library folder");

    private async Task ImportFolderLibraryAsync(string format, string title)
    {
        if (DataContext is not LibrariesViewModel vm)
            return;

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFolder> picked = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await vm.ImportFromFolderAsync(format, path);
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
