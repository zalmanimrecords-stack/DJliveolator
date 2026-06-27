using System.IO;
using System.Reactive.Concurrency;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;

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

    [AvaloniaFact]
    public void Render_dj_console_deck_with_loaded_track_to_png()
    {
        // Run feedback synchronously so the loaded-track header (title + artist + meta) is composed
        // before the frame is captured.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        var deckVm = new DeckViewModel(0, dispatcher,
            trackInfo: _ => new DeckTrackInfo(
                "Impala (Relativ & V-society Remix)", "140.0", "8A", "7:30", Artist: "Protoculture"));
        var deck = new DjDeckView { DataContext = deckVm };

        var window = new Window
        {
            Width = 420,
            Height = 760,
            Content = new Border { Padding = new Thickness(16), Child = deck },
        };
        window.Show();
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 140, Argument: @"C:\impala.flac"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Protoculture", deckVm.Artist);

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "dj-deck-loaded.png");
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
