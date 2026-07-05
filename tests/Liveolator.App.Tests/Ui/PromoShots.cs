using System;
using System.IO;
using System.Reactive.Concurrency;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// High-resolution captures for the marketing video (scripts/build-promo-video.ps1).
/// Unlike the 1:1 design-parity shots, these render at 2x DPI so they stay sharp when
/// scaled onto a 1080p frame, and the decks carry loaded-track state so the promo
/// doesn't show "No track loaded".
/// </summary>
public class PromoShots
{
    [AvaloniaFact]
    public void Render_loaded_console_for_promo()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        var deckA = new DeckViewModel(0, dispatcher,
            trackInfo: _ => new DeckTrackInfo(
                "Impala (Relativ & V-society Remix)", "140.0", "8A", "7:30", Artist: "Protoculture"));
        var deckB = new DeckViewModel(1, dispatcher,
            trackInfo: _ => new DeckTrackInfo(
                "Vertigo (Extended Mix)", "140.0", "9A", "6:45", Artist: "Atmos"));

        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,220,*"),
            Children =
            {
                View(deckA, column: 0, rightMargin: 16),
                View(new MixerViewModel(dispatcher), column: 1, rightMargin: 16),
                View(deckB, column: 2),
            },
        };

        var window = new Window
        {
            Width = 1920,
            Height = 820,
            Content = new Border { Padding = new Thickness(32), Child = controls },
        };
        window.Show();
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 140, Argument: @"C:\impala.flac"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 140, Argument: @"C:\vertigo.flac"));
        Dispatcher.UIThread.RunJobs();

        Save(window, "promo-console.png");
    }

    [AvaloniaFact]
    public void Render_loaded_deck_for_promo()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        var deckVm = new DeckViewModel(0, dispatcher,
            trackInfo: _ => new DeckTrackInfo(
                "Impala (Relativ & V-society Remix)", "140.0", "8A", "7:30", Artist: "Protoculture"));

        var window = new Window
        {
            Width = 640,
            Height = 810,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = new ContentControl { Content = deckVm },
            },
        };
        window.Show();
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 140, Argument: @"C:\impala.flac"));
        Dispatcher.UIThread.RunJobs();

        Save(window, "promo-deck.png");
    }

    [AvaloniaFact]
    public void Render_wide_waveform_for_promo()
    {
        Application app = Application.Current ?? throw new InvalidOperationException("No Application booted.");
        var body = (IBrush)app.FindResource("Waveform")!;
        var ahead = (IBrush)app.FindResource("WaveformAhead")!;
        var kickBrush = (IBrush)app.FindResource("Kick")!;
        var midBrush = (IBrush)app.FindResource("WaveMid")!;
        var highBrush = (IBrush)app.FindResource("WaveHigh")!;
        var playheadBrush = (IBrush)app.FindResource("WavePlayhead")!;
        var beatBrush = (IBrush)app.FindResource("BeatMark")!;
        var downbeatBrush = (IBrush)app.FindResource("DownbeatMark")!;
        var well = (IBrush)app.FindResource("S2")!;

        const int n = 1024;
        var peaks = new float[n];
        var kick = new float[n];
        var mid = new float[n];
        var high = new float[n];
        // Deterministic jitter so the synthetic strip has real-track texture instead of
        // clean sine scallops.
        var rng = new Random(42);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            double noise = 0.65 + 0.35 * rng.NextDouble();
            peaks[i] = (float)((0.30 + 0.50 * Math.Abs(Math.Sin(t * Math.PI * 7)) * (0.6 + 0.4 * Math.Sin(t * Math.PI * 2))) * noise);
            mid[i] = (float)(peaks[i] * (0.55 + 0.30 * rng.NextDouble()));
            high[i] = (float)((0.20 + 0.45 * Math.Abs(Math.Sin(t * Math.PI * 48))) * (0.45 + 0.55 * rng.NextDouble()));
        }
        for (int i = 0; i < n; i += 64)
        {
            kick[i] = i % 256 == 0 ? 1.0f : 0.88f;
            if (i + 1 < n) kick[i + 1] = 0.55f;
        }

        const int beats = 64;
        var grid = new double[beats + 1];
        for (int i = 0; i <= beats; i++)
            grid[i] = i / (double)beats;

        WaveformStrip Strip(bool combAtTop) => new()
        {
            Peaks = peaks,
            KickPeaks = kick,
            MidPeaks = mid,
            HighPeaks = high,
            BeatGrid = grid,
            Progress = 0.45,
            CombAtTop = combAtTop,
            Folded = true,
            BarBrush = ahead,
            PlayedBrush = body,
            KickBrush = kickBrush,
            MidBrush = midBrush,
            HighBrush = highBrush,
            PlayheadBrush = playheadBrush,
            BeatBrush = beatBrush,
            DownbeatBrush = downbeatBrush,
        };

        var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
        stack.Children.Add(new Border { Height = 150, Background = well, Child = Strip(combAtTop: false) });
        stack.Children.Add(new Border { Height = 150, Background = well, Margin = new Thickness(0, 2, 0, 0), Child = Strip(combAtTop: true) });

        var window = new Window { Width = 1920, Height = 302, Content = stack };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Save(window, "promo-waveform.png");
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

    // CaptureRenderedFrame (1:1) is the only rendering path that composes text and custom-drawn
    // controls faithfully under the headless Skia platform; RenderTargetBitmap at a higher DPI
    // drops text and button chrome. Size the windows video-large instead of scaling up.
    private static void Save(Window window, string fileName)
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, fileName);

        window.CaptureRenderedFrame()?.Save(outputPath);
        window.Close();

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
