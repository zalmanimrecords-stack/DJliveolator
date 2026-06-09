using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Liveolator.App.Features.Shared;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow() => InitializeComponent();

    /// <summary>Sets the prompt text and the confirm button's label before the dialog is shown.</summary>
    public void Configure(string title, string message, string confirmLabel)
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
