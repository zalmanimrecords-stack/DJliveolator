using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;
using Liveolator.App.Skins;
using Liveolator.Core.Skins;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// End-to-end proof of the "author a control look via MCP" loop (doc 30): takes parametric
/// <see cref="ControlSkinFile"/>s as an agent would write them, maps each to brushes via
/// <see cref="ControlSkinBrushes"/>, applies them to the real vector <see cref="Knob"/> / <see cref="Fader"/>,
/// and writes artifacts/ui-shots/control-skins.png — two distinct authored looks rendered side by side.
/// </summary>
public sealed class ControlSkinShot
{
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    private static readonly ControlSkinFile CobaltKnob = new()
    {
        Name = "Cobalt Knob", Kind = ControlSkinKind.Knob,
        Accent = "#2F80F6", Track = "#26303F", Pointer = "#E7ECF3", Body = "#12171F",
    };

    private static readonly ControlSkinFile EmberKnob = new()
    {
        Name = "Ember Knob", Kind = ControlSkinKind.Knob,
        Accent = "#E8821A", Track = "#2A1E10", Pointer = "#FFE6C7", Body = "#1A1206",
    };

    [AvaloniaFact]
    public void Render_authored_skins_to_png()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center };

        foreach (ControlSkinFile knobSkin in new[] { CobaltKnob, EmberKnob })
        {
            var knob = new Knob { Value = 0.68, Width = 72, Height = 72 };
            ControlSkinBrushes.From(knobSkin).ApplyTo(knob);
            row.Children.Add(knob);
        }

        // A slider skin sharing the cobalt accent, to show a coherent authored pair.
        var sliderSkin = new ControlSkinFile { Name = "Cobalt Slider", Kind = ControlSkinKind.Slider, Accent = "#2F80F6", Track = "#1A2130", Body = "#E7ECF3" };
        var fader = new Fader { Value = 0.62, Width = 46, Height = 200 };
        ControlSkinBrushes.From(sliderSkin).ApplyTo(fader);
        row.Children.Add(fader);

        var window = new Window
        {
            Background = AppBg,
            Width = 460,
            Height = 280,
            Content = new Border { Padding = new Thickness(28), Child = row },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "control-skins.png");
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();

        Assert.True(File.Exists(outPath));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
