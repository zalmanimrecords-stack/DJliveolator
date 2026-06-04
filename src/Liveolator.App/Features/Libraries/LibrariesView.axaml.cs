using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Libraries;

public partial class LibrariesView : UserControl
{
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
}
