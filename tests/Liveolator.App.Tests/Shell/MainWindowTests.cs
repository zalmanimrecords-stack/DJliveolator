using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Liveolator.App.Shell;

namespace Liveolator.App.Tests.Shell;

public sealed class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData(typeof(TextBox))]
    [InlineData(typeof(ComboBox))]
    [InlineData(typeof(NumericUpDown))]
    [InlineData(typeof(AutoCompleteBox))]
    public void Tab_DoesNotCycleScreens_WhenAnEditableControlIsFocused(System.Type controlType)
    {
        var focused = (Avalonia.Input.IInputElement)System.Activator.CreateInstance(controlType)!;
        Assert.False(MainWindow.ShouldCycleScreensOnTab(focused)); // Tab must traverse fields, not switch tabs
    }

    [AvaloniaFact]
    public void Tab_CyclesScreens_WhenFocusIsOnChromeOrNothing()
    {
        Assert.True(MainWindow.ShouldCycleScreensOnTab(new Button())); // chrome → Tab cycles screens
        Assert.True(MainWindow.ShouldCycleScreensOnTab(null));          // nothing focused → cycles screens
    }

    [AvaloniaFact]
    public void StartsInFullScreen()
    {
        var window = new MainWindow();

        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.Equal(SystemDecorations.None, window.SystemDecorations);
    }

    [AvaloniaFact]
    public void FullScreenToggle_SwitchesBetweenBorderlessFullScreenAndWindow()
    {
        var window = new MainWindow();

        window.ToggleFullScreen();

        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        Assert.Equal("FULL", window.FindControl<Button>("FullScreenButton")!.Content);

        window.ToggleFullScreen();

        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        Assert.Equal("WINDOW", window.FindControl<Button>("FullScreenButton")!.Content);
    }
}
