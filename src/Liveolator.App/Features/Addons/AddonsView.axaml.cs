using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Addons;

public partial class AddonsView : UserControl
{
    // Image types a custom VU-meter face may be supplied in (decoded by SkiaSharp).
    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" },
    };

    public AddonsView() => InitializeComponent();

    // The per-row "Settings" button selects that add-on so its panel shows. Code-behind keeps the
    // selection wiring simple (no parent-VM binding from inside the row template).
    private void OnAddonSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AddonItemViewModel item } && DataContext is AddonsViewModel vm)
            vm.SelectedAddon = item;
    }

    // File picking is view-bound (needs the TopLevel); the chosen path is handed to the UI-free
    // view-model, which persists + applies it. Mirrors VisualLibraryView.OnAddFolder.
    private async void OnBrowseFace(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddonsViewModel vm)
            return;

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose VU-meter background image",
                AllowMultiple = false,
                FileTypeFilter = new[] { ImageFiles },
            });

        if (picked.Count == 0)
            return;

        string? path = picked[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            await vm.VuMeterSettings.ChooseImageAsync(path);
    }
}
