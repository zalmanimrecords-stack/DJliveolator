using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Libraries;

public partial class LibrariesView : UserControl
{
    // Single live instance, so repeated "Folders" clicks focus the open window rather than stack.
    private FoldersStatusWindow? _foldersWindow;

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
}
