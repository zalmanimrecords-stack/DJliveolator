using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Beautiful "loaded" marketing shots of the REAL DJ console (DjConsoleView bound to a PerformanceDeckSet):
/// dual 3-band waveforms + two decks with tracks loaded + the mixer, beatmatched. Uses a synthetic waveform
/// provider (no native decode) so the hero strips render with real texture. These feed the site showcase +
/// hero so the screenshots show the app in use instead of "No track loaded".
///
/// Named *UiShots so the release screenshot capture (sync-website-screenshots.ps1 --filter UiShots)
/// regenerates them alongside the tab shots, keeping the loaded images current with the UI.
/// </summary>
public class ShowcaseUiShots
{
    [AvaloniaFact]
    public void Render_loaded_live_console() =>
        Capture("00-LIVE-loaded.png",
            (@"C:\Music\Protoculture - Impala (Relativ & V-Society Remix).flac", 128.0),
            (@"C:\Music\Atmos - Vertigo (Extended Mix).flac", 128.0));

    [AvaloniaFact]
    public void Render_loaded_dj_console() =>
        Capture("01-DJ-loaded.png",
            (@"C:\Music\Vini Vici - Great Spirit.flac", 138.0),
            (@"C:\Music\Astrix - Deep Jungle Walk.flac", 138.0));

    private static void Capture(string fileName, (string path, double bpm) a, (string path, double bpm) b)
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher, new SyntheticWaveformProvider(), deckTransportEnabled: true);

        var view = new DjConsoleView { DataContext = decks };
        var window = new Window
        {
            Width = 1400,
            Height = 660,
            Content = new Border { Padding = new Avalonia.Thickness(16), Child = view },
        };
        window.Show();

        // Load a named track on each deck (title falls back to the file name — no catalog needed) at matched
        // tempos so the BPM readouts light the green "beatmatched" highlight, then set both playing.
        Load(dispatcher, 0, a.path, a.bpm);
        Load(dispatcher, 1, b.path, b.bpm);

        // The waveform overview decodes on a background Task; pump until both decks have their kick band.
        WaitFor(() => decks.DeckA.KickPeaks is { Count: > 0 } && decks.DeckB.KickPeaks is { Count: > 0 });
        Dispatcher.UIThread.RunJobs();

        string outputPath = ShotPath(fileName);
        window.CaptureRenderedFrame()?.Save(outputPath);
        window.Close();
        Assert.True(File.Exists(outputPath));
    }

    private static void Load(FakeDispatcher dispatcher, int slot, string path, double bpm)
    {
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: bpm, Argument: path));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: bpm, Argument: "60|200"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
    }

    // Pump the UI thread until the condition holds (the waveform decode is async), with a hard timeout.
    private static void WaitFor(Func<bool> done)
    {
        for (int i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    // Synthetic 3-band overview with real-track texture (mirrors PromoShots' generator), so the hero strips
    // draw a believable waveform without decoding a real file. Seeded by path so the two decks differ.
    private sealed class SyntheticWaveformProvider : IWaveformProvider
    {
        public Task<WaveformOverview> GetOverviewAsync(string filePath, int bucketCount, CancellationToken cancellationToken = default)
        {
            int n = Math.Max(1, bucketCount);
            var peaks = new float[n];
            var low = new float[n];
            var mid = new float[n];
            var high = new float[n];
            var rng = new Random(filePath.Length * 31 + 7);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                double noise = 0.65 + 0.35 * rng.NextDouble();
                peaks[i] = (float)((0.30 + 0.50 * Math.Abs(Math.Sin(t * Math.PI * 7)) * (0.6 + 0.4 * Math.Sin(t * Math.PI * 2))) * noise);
                mid[i] = (float)(peaks[i] * (0.55 + 0.30 * rng.NextDouble()));
                high[i] = (float)((0.20 + 0.45 * Math.Abs(Math.Sin(t * Math.PI * 48))) * (0.45 + 0.55 * rng.NextDouble()));
            }
            int step = Math.Max(1, n / 128);
            for (int i = 0; i < n; i += step)
            {
                low[i] = (i / step) % 4 == 0 ? 1.0f : 0.85f; // a kick every few buckets, downbeats hotter
                if (i + 1 < n) low[i + 1] = 0.5f;
            }
            return Task.FromResult(new WaveformOverview(peaks, DurationSeconds: 450, LowPeaks: low, MidPeaks: mid, HighPeaks: high));
        }
    }

    private static string ShotPath(string fileName)
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        return Path.Combine(outDir, fileName);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
