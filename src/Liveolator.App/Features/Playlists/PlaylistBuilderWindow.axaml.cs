using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Playlists;

public partial class PlaylistBuilderWindow : Window
{
    public PlaylistBuilderWindow() => InitializeComponent();

    // Folder picking is view-bound (it needs the TopLevel); the chosen path goes to the view-model.
    private async void OnCopyToComputer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistBuilderViewModel vm || !vm.CanCopyToComputer)
            return;

        IStorageFolder? start = await vm.LastLocalCopyFolderAsync() is { } last
            ? await StorageProvider.TryGetFolderFromPathAsync(last)
            : null;
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Copy the set to this folder", SuggestedStartLocation = start });
        if (picked.Count == 0)
            return;

        // Same fallback as the library's Add folder: TryGetLocalPath is null for some picks.
        IStorageFolder folder = picked[0];
        string? path = folder.TryGetLocalPath()
            ?? (folder.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null);
        await vm.CopyToComputerAsync(path ?? string.Empty);
    }
}
