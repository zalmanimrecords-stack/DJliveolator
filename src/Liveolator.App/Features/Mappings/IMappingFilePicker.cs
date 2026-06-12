using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Liveolator.App.Features.Mappings;

/// <summary>
/// Picks a file path for exporting/importing a MIDI mapping (doc 05). A seam so the Avalonia-free-ish
/// <c>MappingsViewModel</c> stays testable: the real implementation drives the platform file dialog, a
/// fake supplies a path. Returns null when the user cancels or no window is available.
/// </summary>
public interface IMappingFilePicker
{
    Task<string?> PickExportPathAsync(string suggestedFileName);
    Task<string?> PickImportPathAsync();
}

/// <summary>Default <see cref="IMappingFilePicker"/> over the main window's <see cref="IStorageProvider"/>.</summary>
public sealed class StorageProviderMappingFilePicker : IMappingFilePicker
{
    private static readonly FilePickerFileType MidiMapType =
        new("Liveolator MIDI map") { Patterns = new[] { "*.json" } };

    public async Task<string?> PickExportPathAsync(string suggestedFileName)
    {
        if (MainWindow()?.StorageProvider is not { CanSave: true } storage)
            return null;

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export MIDI mapping",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { MidiMapType },
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickImportPathAsync()
    {
        if (MainWindow()?.StorageProvider is not { CanOpen: true } storage)
            return null;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import MIDI mapping",
            AllowMultiple = false,
            FileTypeFilter = new[] { MidiMapType },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static Window? MainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
