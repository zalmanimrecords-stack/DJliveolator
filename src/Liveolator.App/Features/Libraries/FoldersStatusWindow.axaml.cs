using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Libraries;

public partial class FoldersStatusWindow : Window
{
    public FoldersStatusWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // Folder picking is view-bound (needs a TopLevel); this window is itself a TopLevel and shares the
    // live LibrariesViewModel, so it adds folders through the same vm.AddFolder flow as LibrariesView.
    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibrariesViewModel vm)
            return;

        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Add music folder", AllowMultiple = true });

        foreach (IStorageFolder folder in picked)
        {
            string? path = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                vm.AddFolder(path);
        }
    }
}
