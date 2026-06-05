using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Liveolator.App.Features.Libraries;

public partial class FoldersStatusWindow : Window
{
    public FoldersStatusWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
