using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Renders the deck <see cref="WaveformStrip"/> with a synthetic 3-band pattern (kick / mid body /
/// high caps) plus a 4/4 beat grid, and saves a PNG, so the VirtualDJ look — red kicks glowing in front
/// of the green body with blue/cyan caps, over the bottom CBG comb (grey beat teeth + red downbeat
/// blocks) and a near-white playhead — can be eyeballed against the design intent (the tabs in
/// <see cref="UiShots"/> show "NO TRACK", so they can't exercise the waveform colours). It pulls the real
/// <c>Waveform</c>/<c>WaveformAhead</c>/<c>Kick</c>/<c>WaveMid</c>/<c>WaveHigh</c>/<c>WavePlayhead</c>/
/// <c>BeatMark</c>/<c>DownbeatMark</c> brush tokens from the booted App, so it also proves those resolve.
/// </summary>
public class WaveformShot
{
    [AvaloniaFact]
    public void Capture_waveform_with_kick_band()
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

        const int n = 256;
        var peaks = new float[n];
        var kick = new float[n];
        var mid = new float[n];
        var high = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            // A musical-ish body envelope so the strip looks like a real track, not a flat block.
            peaks[i] = (float)(0.30 + 0.50 * Math.Abs(Math.Sin(t * Math.PI * 7)) * (0.6 + 0.4 * Math.Sin(t * Math.PI * 2)));
            mid[i] = peaks[i] * 0.75f;
            // Hats: a shimmering high texture, denser on the off-beats.
            high[i] = (float)(0.25 + 0.45 * Math.Abs(Math.Sin(t * Math.PI * 32)));
        }
        // A kick on every 16th column (a steady four-on-the-floor) with a short tail, everything else
        // silent — only the kicks light up in front. Every 4th kick is a hot one (white-hot core).
        for (int i = 0; i < n; i += 16)
        {
            kick[i] = i % 64 == 0 ? 1.0f : 0.88f;
            if (i + 1 < n) kick[i + 1] = 0.55f;
        }

        // A 4/4 beat grid (one beat every 1/64th of the track) so the bottom CBG comb renders: short grey
        // beat teeth with a broad red downbeat block on every 4th — aligned with the four-on-the-floor kicks.
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

        // The combined "butterfly": Deck A folds UP (comb at its bottom), Deck B folds DOWN (comb at its
        // top); the two combs meet in the middle and the waves mirror outward.
        var stack = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
        stack.Children.Add(new Border { Height = 84, Background = well, Child = Strip(combAtTop: false) });
        stack.Children.Add(new Border { Height = 84, Background = well, Margin = new Avalonia.Thickness(0, 1, 0, 0), Child = Strip(combAtTop: true) });

        var window = new Window { Width = 720, Height = 170, Content = stack };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
            Directory.CreateDirectory(outDir);
            frame!.Save(Path.Combine(outDir, "waveform-kick.png"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The DJ PRO kick-focused waveform (VirtualDJ style): the synthetic 3-band pattern rendered FOLDED
    /// (single-sided) with the kicks spiking in RED over a BLUE body — the owner's chosen reference look.
    /// Explicit colours (theme-independent) so the preview shows the owner's blue-theme result regardless of
    /// the default theme the test boots.
    /// </summary>
    [AvaloniaFact]
    public void Capture_djpro_kickfocus_waveform()
    {
        Application app = Application.Current ?? throw new InvalidOperationException("No Application booted.");
        // Explicit palette (theme-independent) matching the owner's blue theme: kick = red (spikes),
        // body = blue (mid), highs = cyan caps.
        var kickBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xE2, 0x3B, 0x2E));
        var midBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));
        var highBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xA0, 0x36, 0xA6, 0xE8));
        var playheadBrush = (IBrush)app.FindResource("WavePlayhead")!;
        var beatBrush = (IBrush)app.FindResource("BeatMark")!;
        var downbeatBrush = (IBrush)app.FindResource("DownbeatMark")!;
        var body = (IBrush)app.FindResource("Waveform")!;
        var ahead = (IBrush)app.FindResource("WaveformAhead")!;
        var well = (IBrush)app.FindResource("S2")!;

        const int n = 256;
        var peaks = new float[n];
        var kick = new float[n];
        var mid = new float[n];
        var high = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            peaks[i] = (float)(0.30 + 0.50 * Math.Abs(Math.Sin(t * Math.PI * 7)) * (0.6 + 0.4 * Math.Sin(t * Math.PI * 2)));
            mid[i] = peaks[i] * 0.75f;
            high[i] = (float)(0.25 + 0.45 * Math.Abs(Math.Sin(t * Math.PI * 32)));
        }
        for (int i = 0; i < n; i += 16)
        {
            kick[i] = i % 64 == 0 ? 1.0f : 0.88f;
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
            Folded = true, // folded (single-sided) — the red/blue kick-spike look
            BodyScale = 0.65, // compress the body so the red kicks spike above it
            BarBrush = ahead,
            PlayedBrush = body,
            KickBrush = kickBrush,
            MidBrush = midBrush,
            HighBrush = highBrush,
            PlayheadBrush = playheadBrush,
            BeatBrush = beatBrush,
            DownbeatBrush = downbeatBrush,
        };

        var stack = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
        stack.Children.Add(new Border { Height = 140, Background = well, Child = Strip(combAtTop: false) });
        stack.Children.Add(new Border { Height = 140, Background = well, Margin = new Avalonia.Thickness(0, 1, 0, 0), Child = Strip(combAtTop: true) });

        var window = new Window { Width = 900, Height = 285, Content = stack };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
            Directory.CreateDirectory(outDir);
            frame!.Save(Path.Combine(outDir, "waveform-djpro-kick.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
