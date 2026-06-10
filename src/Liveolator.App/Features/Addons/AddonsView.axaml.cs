using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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

    // Copies a single guideline paragraph to the clipboard. The text rides on the button's Tag (bound to
    // the matching view-model string), so one handler serves all the Copy buttons. Clipboard access is
    // view-bound (needs the TopLevel), so it lives here rather than in the UI-free view-model.
    private async void OnCopyText(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || string.IsNullOrEmpty(text))
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}
