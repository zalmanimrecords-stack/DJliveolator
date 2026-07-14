using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Renders the shared button styles without starting native audio or MIDI services.
/// The image is a quick visual regression aid for depth, active state, and hierarchy.
/// </summary>
public class HardwareButtonsShot
{
    [AvaloniaFact]
    public void Render_button_states_to_png()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                StyledButton("spartan", "LOAD"),
                StyledButton("accent", "SCAN"),
                StyledButton("spartan", "DISABLED", isEnabled: false),
            },
        };

        var performanceKeys = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                StyledButton("key", "CUE", 86, 44),
                StyledButton("key", "LOOP", 86, 44, "on"),
                StyledButton("play", "PLAY", 86, 44),
                StyledButton("key", "SYNC", 86, 44, isEnabled: false),
            },
        };

        var pads = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                StyledButton("pad", string.Empty, 52, 34),
                StyledButton("pad", string.Empty, 52, 34, "has"),
                StyledButton("pad", string.Empty, 52, 34, "on"),
                StyledButton("pad", string.Empty, 52, 34, isEnabled: false),
            },
        };

        var panel = new Border
        {
            Classes = { "panel" },
            Padding = new Thickness(34),
            Child = new StackPanel
            {
                Spacing = 30,
                Children =
                {
                    LabelledRow("ACTIONS", actions),
                    LabelledRow("PERFORMANCE KEYS", performanceKeys),
                    LabelledRow("SCENE PADS", pads),
                },
            },
        };

        var window = new Window
        {
            Width = 600,
            Height = 420,
            Content = new Border { Padding = new Thickness(28), Child = panel },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "hardware-buttons.png");
        window.CaptureRenderedFrame()?.Save(outputPath);

        Assert.True(File.Exists(outputPath));
    }

    private static Control LabelledRow(string label, Control content) =>
        new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = label, Classes = { "section" } },
                content,
            },
        };

    private static Button StyledButton(
        string style,
        string content,
        double width = double.NaN,
        double height = double.NaN,
        string? state = null,
        bool isEnabled = true)
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            IsEnabled = isEnabled,
        };
        button.Classes.Add(style);
        if (state is not null)
            button.Classes.Add(state);
        return button;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
