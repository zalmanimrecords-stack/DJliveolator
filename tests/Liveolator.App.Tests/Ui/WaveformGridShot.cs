using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Features.Live.Modules;

namespace Liveolator.App.Tests.Ui;

// Render smoke test for the waveform beat comb (owner request: bar lines CYAN, every 4th bar RED, faint
// grey beats between). Eyeball artifacts/ui-shots/waveform-grid-bars.png.
public class WaveformGridShot
{
    [AvaloniaFact]
    public void Render_waveform_grid_bar_colours_to_png()
    {
        // 40 beats evenly spaced across the visible strip; downbeat on the first beat (index 0). At this
        // width each beat is ~30 px, so beats + bars + phrases all resolve.
        double[] grid = Enumerable.Range(0, 40).Select(i => 0.02 + i * 0.024).ToArray();
        float[] peaks = Enumerable.Range(0, 400)
            .Select(i => (float)(0.35 + 0.5 * Math.Abs(Math.Sin(i * 0.15)))).ToArray();

        var strip = new Liveolator.App.Controls.WaveformStrip
        {
            Peaks = peaks,
            BeatGrid = grid,
            DownbeatOffset = 0,
            Width = 1200,
            Height = 130,
        };

        var window = new Window
        {
            Width = 1232,
            Height = 170,
            Content = new Border { Padding = new Thickness(16), Background = Brushes.Black, Child = strip },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Capture(window, "waveform-grid-bars.png");
    }

    // Deck A (top, comb at the bottom) and deck B (bottom, comb at the top) — the stacked butterfly, with
    // the SAME grid / brushes / body scale (as the shared DeckWaveform module drives them in the app).
    // Confirms both decks render the beat grid by the IDENTICAL rules, mirrored around the shared middle.
    [AvaloniaFact]
    public void Render_ab_waveform_pair_shares_the_same_grid_rules_to_png()
    {
        double[] grid = Enumerable.Range(0, 40).Select(i => 0.02 + i * 0.024).ToArray();
        float[] peaks = Enumerable.Range(0, 400)
            .Select(i => (float)(0.35 + 0.5 * Math.Abs(Math.Sin(i * 0.15)))).ToArray();

        var stack = new Grid { RowDefinitions = new RowDefinitions("*,*") };
        var deckA = MakeStrip(grid, peaks, combAtTop: false); // top: grows up, comb at bottom
        var deckB = MakeStrip(grid, peaks, combAtTop: true);  // bottom: grows down, comb at top
        Grid.SetRow(deckA, 0);
        Grid.SetRow(deckB, 1);
        stack.Children.Add(deckA);
        stack.Children.Add(deckB);

        var window = new Window
        {
            Width = 1232,
            Height = 300,
            Content = new Border { Padding = new Thickness(16), Background = Brushes.Black, Child = stack },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Capture(window, "waveform-ab-grid.png");
    }

    // The DJ PRO kick zipper, laid out like DjProView: folded pairs, A's comb at its bottom and B's at its top,
    // 5 px apart, 62 px strips, body 0.65, both needles dead-centre at the TRUE playhead, a kick on every beat.
    // (1) locked: A's ticks run straight on into B's. (2) B 20 ms late: every pair splits by the same amount.
    // (3) B at 126 BPM against 125: together at the needle, fanning apart toward the edges.
    // Eyeball artifacts/ui-shots/waveform-kick-zipper.png.
    [AvaloniaFact]
    public void Render_kick_zipper_to_png()
    {
        const double beat125 = 60.0 / 125.0, beat126 = 60.0 / 126.0;
        var a = new ZipperDeck(125, 240, FirstBeat: 0.12, Playhead: 0.12 + (200.3 * beat125));
        var locked = new ZipperDeck(125, 300, FirstBeat: 0.31, Playhead: 0.31 + (150.3 * beat125)); // same phase, another track
        var late = locked with { Playhead = locked.Playhead - 0.020 };
        var faster = new ZipperDeck(126, 300, FirstBeat: 0.31, Playhead: 0.31 + (150.3 * beat126));

        var pairs = new StackPanel { Spacing = 14 };
        pairs.Children.Add(ZipperPair("LOCKED", a, locked));
        pairs.Children.Add(ZipperPair("B 20 ms LATE", a, late));
        pairs.Children.Add(ZipperPair("B 126 BPM vs A 125", a, faster));

        var window = new Window
        {
            Width = 1432,
            Height = 520,
            Content = new Border { Padding = new Thickness(16), Background = Brushes.Black, Child = pairs },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Capture(window, "waveform-kick-zipper.png");
    }

    private sealed record ZipperDeck(double Bpm, double Duration, double FirstBeat, double Playhead);

    private static Control ZipperPair(string caption, ZipperDeck a, ZipperDeck b)
    {
        var pair = new StackPanel { Spacing = 5 };
        pair.Children.Add(new TextBlock { Text = caption, FontSize = 10, Foreground = Token<IBrush>("Dim") });
        pair.Children.Add(ZipperStrip(a, combAtTop: false));
        pair.Children.Add(ZipperStrip(b, combAtTop: true));
        return pair;
    }

    // One DeckWaveform look-alike: the same border, well gradient, theme brushes and 7 s window DJ PRO shows.
    private static Control ZipperStrip(ZipperDeck deck, bool combAtTop)
    {
        const double zoomSeconds = 7.0, bucketsPerSecond = 150.0;
        double beat = 60.0 / deck.Bpm;
        double[] kickSeconds = Enumerable.Range(0, (int)((deck.Duration - deck.FirstBeat) / beat))
            .Select(i => deck.FirstBeat + (i * beat)).ToArray();

        int n = (int)(deck.Duration * bucketsPerSecond);
        var rng = new Random(combAtTop ? 7 : 3);
        var kick = new float[n];
        var mid = new float[n];
        var high = new float[n];
        for (int i = 0; i < n; i++)
        {
            double sinceBeat = ((i / bucketsPerSecond) - deck.FirstBeat) % beat;
            mid[i] = (float)((0.35 + (0.35 * Math.Exp(-sinceBeat * 6))) * (0.7 + (0.3 * rng.NextDouble())));
            high[i] = (float)(0.25 + (0.35 * rng.NextDouble()));
        }
        float[] attack = { 1.0f, 0.8f, 0.55f, 0.3f };
        foreach (double t in kickSeconds)
        {
            int k = (int)Math.Round(t * bucketsPerSecond);
            for (int j = 0; j < attack.Length && k + j < n; j++)
                kick[k + j] = attack[j];
        }

        var strip = new Liveolator.App.Controls.WaveformStrip
        {
            Peaks = mid,
            KickPeaks = kick,
            MidPeaks = mid,
            HighPeaks = high,
            BarBrush = Token<IBrush>("WaveformAhead"),
            PlayedBrush = Token<IBrush>("Waveform"),
            KickBrush = Token<IBrush>("Kick"),
            MidBrush = Token<IBrush>("Accent"),
            HighBrush = Token<IBrush>("WaveHigh"),
            PlayheadBrush = Token<IBrush>("WavePlayhead"),
            BeatBrush = Token<IBrush>("BeatMark"),
            BarLineBrush = Token<IBrush>("BarLineMark"),
            DownbeatBrush = Token<IBrush>("DownbeatMark"),
            BodyScale = 0.65,
            Folded = true,
            CombAtTop = combAtTop,
            BeatGrid = BeatGridCalculator.BeatFractions(deck.Bpm, deck.Duration, deck.FirstBeat),
            KickMarkers = BeatGridCalculator.KickFractions(kickSeconds, deck.Duration),
            Progress = deck.Playhead / deck.Duration,
            ZoomWindow = zoomSeconds / deck.Duration,
        };

        return new Border
        {
            Height = 62,
            BorderBrush = Token<IBrush>("Hair"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Token<Color>("BgColor"), 0),
                    new GradientStop(Token<Color>("S2Color"), 0.5),
                    new GradientStop(Token<Color>("S1Color"), 1),
                },
            },
            Child = strip,
        };
    }

    private static T Token<T>(string key) => (T)Application.Current!.FindResource(key)!;

    private static Liveolator.App.Controls.WaveformStrip MakeStrip(double[] grid, float[] peaks, bool combAtTop)
        => new()
        {
            Peaks = peaks,
            BeatGrid = grid,
            DownbeatOffset = 0,
            Folded = true,
            CombAtTop = combAtTop,
            BodyScale = 0.65,
        };

    private static void Capture(Window window, string fileName)
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, fileName);
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
