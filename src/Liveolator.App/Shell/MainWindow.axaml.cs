using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Liveolator.App.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Tab cycles the app's screens (Shift+Tab goes back). Handle the tunnelling phase so the
        // window sees the key before the focused control's default Tab focus-traversal consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            vm.SelectPreviousTab();
        }
        else
        {
            vm.SelectNextTab();
        }

        e.Handled = true;
    }

    private void OnFullScreenClick(object? sender, RoutedEventArgs e)
        => ToggleFullScreen();

    internal void ToggleFullScreen()
    {
        bool enterFullScreen = WindowState != WindowState.FullScreen;
        SystemDecorations = enterFullScreen
            ? SystemDecorations.None
            : SystemDecorations.Full;
        WindowState = enterFullScreen
            ? WindowState.FullScreen
            : WindowState.Normal;
        FullScreenButton.Content = enterFullScreen ? "WINDOW" : "FULL";
    }
}
