using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;

namespace Liveolator.App.Tests.Ui;

public class DjDeckShot
{
    [AvaloniaFact]
    public void Render_dj_console_deck_to_png()
    {
        var deck = new DjDeckView { DataContext = new DeckViewModel(0, new FakeDispatcher()) };

        var window = new Window
        {
            Width = 420,
            Height = 760,
            Content = new Border { Padding = new Thickness(16), Child = deck },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "dj-deck.png");
        window.CaptureRenderedFrame()?.Save(outputPath);

        Assert.True(File.Exists(outputPath));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
