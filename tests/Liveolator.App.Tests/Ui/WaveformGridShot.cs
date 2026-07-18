using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

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

    // Demonstrates the render-time beat-phase lock (owner: "same BPM ⇒ the grids must move together").
    // Top = master (on a beat). Middle = the follower UNLOCKED — a quarter-beat off, its grid clearly offset.
    // Bottom = the follower LOCKED via WaveformSyncScroll.FollowerOffset — its grid snapped onto the master's.
    [AvaloniaFact]
    public void Render_synced_decks_grid_lock_to_png()
    {
        const double duration = 40.0, firstBeat = 0.0, bpm = 125.0; // beatSeconds 0.48
        double beatFrac = 60.0 / bpm / duration;                    // one beat as a track fraction
        double[] grid = Enumerable.Range(0, 83).Select(i => i * beatFrac).ToArray();
        float[] peaks = Enumerable.Range(0, 400)
            .Select(i => (float)(0.35 + 0.5 * Math.Abs(Math.Sin(i * 0.15)))).ToArray();
        const double zoom = 0.24; // ~20 beats visible

        double masterProgress = 0.30;                    // lands on beat 25 (phase 0)
        double followerRaw = masterProgress + 0.25 * beatFrac; // a quarter-beat late
        double offset = Liveolator.App.Features.Live.Modules.WaveformSyncScroll.FollowerOffset(
            masterProgress, duration, firstBeat, bpm, followerRaw, duration, firstBeat, bpm);

        var master = SyncStrip(grid, peaks, masterProgress, zoom);
        var unlocked = SyncStrip(grid, peaks, followerRaw, zoom);
        var locked = SyncStrip(grid, peaks, followerRaw + offset, zoom);

        var stack = new Grid { RowDefinitions = new RowDefinitions("*,*,*") };
        Grid.SetRow(master, 0); Grid.SetRow(unlocked, 1); Grid.SetRow(locked, 2);
        stack.Children.Add(master); stack.Children.Add(unlocked); stack.Children.Add(locked);

        var window = new Window
        {
            Width = 1232,
            Height = 340,
            Content = new Border { Padding = new Thickness(16), Background = Brushes.Black, Child = stack },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Capture(window, "waveform-sync-lock.png");
    }

    private static Liveolator.App.Controls.WaveformStrip SyncStrip(double[] grid, float[] peaks, double progress, double zoom)
        => new()
        {
            Peaks = peaks,
            BeatGrid = grid,
            DownbeatOffset = 0,
            Progress = progress,
            ZoomWindow = zoom,
            BodyScale = 0.65,
        };

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
