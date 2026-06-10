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

        if (e.Key == Key.Escape && DataContext is MainWindowViewModel escapeVm
            && escapeVm.MidiLearn.IsEnabled)
        {
            escapeVm.CancelMidiLearn();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Don't hijack Tab while focus is in an editable/list control — the user needs it there for
        // standard field-to-field focus traversal (e.g. the Settings form, the library filter bar).
        // Consuming it unconditionally broke keyboard navigation app-wide (docs/19 accessibility).
        if (!ShouldCycleScreensOnTab(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
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

    // Bare Tab cycles the app's screens, but only when focus is on chrome or nothing — never when a
    // text-editing or list control owns it, since those need Tab for their own focus traversal.
    // Pure + static so the decision is unit-testable without spinning up a window and focus tree.
    internal static bool ShouldCycleScreensOnTab(IInputElement? focused)
        => focused is not (TextBox or ComboBox or AutoCompleteBox or NumericUpDown);

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
