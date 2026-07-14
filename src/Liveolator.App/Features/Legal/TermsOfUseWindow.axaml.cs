using Avalonia.Controls;
using Avalonia.Interactivity;
using Liveolator.Core.Legal;

namespace Liveolator.App.Features.Legal;

/// <summary>
/// First-launch acceptance gate for the <see cref="TermsOfUse"/> (doc 12). A modal window shown over the
/// main window before use: the performer must explicitly accept to continue, or decline to exit. The
/// dialog result is <c>true</c> only when accepted; closing it any other way (the X, declining) is a
/// decline, so the app never proceeds without consent. Mirrors the <c>ConfirmationWindow</c> pattern.
/// </summary>
public partial class TermsOfUseWindow : Window
{
    public TermsOfUseWindow()
    {
        InitializeComponent();
        Title = TermsOfUse.Title;
        TitleText.Text = $"{TermsOfUse.Title} (v{TermsOfUse.CurrentVersion})";
        TermsText.Text = TermsOfUse.Text;
    }

    private void OnDecline(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);
}
