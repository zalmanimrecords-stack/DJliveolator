using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;

namespace Liveolator.App.Tests.Ui;

public class DjControlsShot
{
    [AvaloniaFact]
    public void Render_decks_and_mixer_to_png()
    {
        var dispatcher = new FakeDispatcher();
        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,220,*"),
            Children =
            {
                View(new DeckViewModel(0, dispatcher), column: 0, rightMargin: 16),
                View(new MixerViewModel(dispatcher), column: 1, rightMargin: 16),
                View(new DeckViewModel(1, dispatcher), column: 2),
            },
        };

        var window = new Window
        {
            Width = 1440,
            Height = 700,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = controls,
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "dj-controls.png");
        window.CaptureRenderedFrame()?.Save(outputPath);

        Assert.True(File.Exists(outputPath));
    }

    private static ContentControl View(object viewModel, int column, double rightMargin = 0)
    {
        var control = new ContentControl
        {
            Content = viewModel,
            Margin = new Thickness(0, 0, rightMargin, 0),
        };
        Grid.SetColumn(control, column);
        return control;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
