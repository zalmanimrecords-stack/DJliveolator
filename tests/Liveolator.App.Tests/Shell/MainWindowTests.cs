using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Liveolator.App.Shell;

namespace Liveolator.App.Tests.Shell;

public sealed class MainWindowTests
{
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
