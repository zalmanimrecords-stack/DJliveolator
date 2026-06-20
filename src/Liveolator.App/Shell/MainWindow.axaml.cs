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

        // Bare number keys 1..N jump straight to the matching tab (1 = first tab). Like Tab cycling, this
        // is suppressed while a text/list control owns focus so digits typed into a field aren't hijacked,
        // and only for an unmodified press so Ctrl/Alt/Shift+digit shortcuts elsewhere are left alone.
        if (e.KeyModifiers == KeyModifiers.None
            && DataContext is MainWindowViewModel numberVm
            && DigitFromKey(e.Key) is int tabNumber
            && ShouldCycleScreensOnTab(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            numberVm.SelectTabByNumber(tabNumber);
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

    // Maps a top-row or numpad digit key (1..9) to its 1-based tab number; null for any other key.
    // Pure + static so the mapping is unit-testable without a window.
    internal static int? DigitFromKey(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
        _ => null,
    };

    private void OnFullScreenClick(object? sender, RoutedEventArgs e)
        => ToggleFullScreen();

    internal void ToggleFullScreen() => SetFullScreen(WindowState != WindowState.FullScreen);

    // Applies a full-screen / windowed state and keeps the toggle button's label in step. Shared by the
    // F11/button toggle and the startup layout restore so both routes stay consistent (decorations + label).
    internal void SetFullScreen(bool fullScreen)
    {
        SystemDecorations = fullScreen ? SystemDecorations.None : SystemDecorations.Full;
        WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;
        FullScreenButton.Content = fullScreen ? "WINDOW" : "FULL";
    }
}
