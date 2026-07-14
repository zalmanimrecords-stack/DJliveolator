using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Liveolator.App.Layout;
using Liveolator.App.Shell;

namespace Liveolator.App.Tests.Shell;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData(Key.D1, 1)]
    [InlineData(Key.D5, 5)]
    [InlineData(Key.D9, 9)]
    [InlineData(Key.NumPad1, 1)]
    [InlineData(Key.NumPad5, 5)]
    public void DigitFromKey_MapsTopRowAndNumpadDigitsToTabNumbers(Key key, int expected)
        => Assert.Equal(expected, MainWindow.DigitFromKey(key));

    [Theory]
    [InlineData(Key.D0)]      // 0 has no tab
    [InlineData(Key.NumPad0)]
    [InlineData(Key.A)]
    [InlineData(Key.Tab)]
    public void DigitFromKey_ReturnsNull_ForNonTabDigitKeys(Key key)
        => Assert.Null(MainWindow.DigitFromKey(key));

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

    [AvaloniaFact]
    public void AlwaysCarriesExactlyOneTierClass()
    {
        var window = new MainWindow();

        // Whatever the window's initial ClientSize resolves to, exactly one tier class is present so
        // descendant selectors always match one — and only one — tier.
        int present = 0;
        foreach (var name in new[] { "compact", "standard", "wide", "ultra" })
            if (window.Classes.Contains(name))
                present++;
        Assert.Equal(1, present);
    }

    [AvaloniaTheory]
    [InlineData(1000, LayoutSizeClass.Compact, "compact")]
    [InlineData(1440, LayoutSizeClass.Standard, "standard")]
    [InlineData(2000, LayoutSizeClass.Wide, "wide")]
    [InlineData(3840, LayoutSizeClass.Ultra, "ultra")]
    public void UpdateSizeClass_SetsExactlyOneTierClass(double width, LayoutSizeClass expected, string styleClass)
    {
        var window = new MainWindow();

        window.UpdateSizeClass(width);

        Assert.Equal(expected, window.CurrentSizeClass);
        Assert.Contains(styleClass, window.Classes);
        // The other three tier classes must be cleared so descendant selectors never double-match.
        foreach (var other in new[] { "compact", "standard", "wide", "ultra" })
            if (other != styleClass)
                Assert.DoesNotContain(other, window.Classes);
    }

    [AvaloniaFact]
    public void UpdateSizeClass_HoldsTierWithinTheDeadBand()
    {
        var window = new MainWindow();
        window.UpdateSizeClass(1440); // force a known Standard tier first

        window.UpdateSizeClass(1160); // just below the raw 1180 boundary, inside the dead-band

        Assert.Equal(LayoutSizeClass.Standard, window.CurrentSizeClass);
        Assert.Contains("standard", window.Classes);
    }

    [AvaloniaFact]
    public void UpdateSizeClass_DefersTierChangeWhileADeckIsPlaying()
    {
        var window = new MainWindow();
        window.UpdateSizeClass(1440); // known Standard baseline
        window.PlayingProbeForTests = () => true; // a deck is playing

        window.UpdateSizeClass(1000); // would normally drop to Compact

        // Held: a resize mid-mix must not jump the layout under the DJ's hands.
        Assert.Equal(LayoutSizeClass.Standard, window.CurrentSizeClass);
        Assert.Contains("standard", window.Classes);

        // Once playback stops, the deferred change is applied on the next resolve.
        window.PlayingProbeForTests = () => false;
        window.UpdateSizeClass(1000);
        Assert.Equal(LayoutSizeClass.Compact, window.CurrentSizeClass);
        Assert.Contains("compact", window.Classes);
    }
}
