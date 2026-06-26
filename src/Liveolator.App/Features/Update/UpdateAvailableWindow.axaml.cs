using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Liveolator.Core.Update;

namespace Liveolator.App.Features.Update;

/// <summary>
/// The modal "a newer version is available" dialog (mirrors <c>ConfirmationWindow</c>'s style). Closes
/// with the chosen <see cref="UpdateDialogChoice"/>, so the caller can download, skip, or defer.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow() => InitializeComponent();

    /// <summary>Fills the dialog from the manifest and the running version before it is shown.</summary>
    public void Configure(UpdateManifest manifest, string currentVersion)
    {
        TitleText.Text = $"Version {manifest.Version} is available";
        MessageText.Text = $"You're running {currentVersion}. A newer build can be downloaded from the website.";
        NotesList.ItemsSource = new List<string>(manifest.Notes);
        NotesList.IsVisible = manifest.Notes.Count > 0;
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close(UpdateDialogChoice.Later);

    private void OnSkip(object? sender, RoutedEventArgs e) => Close(UpdateDialogChoice.Skip);

    private void OnDownload(object? sender, RoutedEventArgs e) => Close(UpdateDialogChoice.Download);
}
