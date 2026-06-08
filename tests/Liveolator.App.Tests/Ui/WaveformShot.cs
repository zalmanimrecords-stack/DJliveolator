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
/// Renders the deck <see cref="WaveformStrip"/> with synthetic peaks + a kick pattern and saves a PNG,
/// so the green-kick-on-yellow-body look can be eyeballed against the design intent (the tabs in
/// <see cref="UiShots"/> show "NO TRACK", so they can't exercise the waveform colours). It pulls the
/// real <c>Waveform</c>/<c>WaveformAhead</c>/<c>Kick</c> brush tokens from the booted App, so it also
/// proves those resources resolve.
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
        var well = (IBrush)app.FindResource("S2")!;

        const int n = 256;
        var peaks = new float[n];
        var kick = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            // A musical-ish body envelope so the strip looks like a real track, not a flat block.
            peaks[i] = (float)(0.30 + 0.50 * Math.Abs(Math.Sin(t * Math.PI * 7)) * (0.6 + 0.4 * Math.Sin(t * Math.PI * 2)));
        }
        // A kick on every 16th column (a steady four-on-the-floor) with a short tail, everything else silent —
        // so only the kicks light up green over the yellow body.
        for (int i = 0; i < n; i += 16)
        {
            kick[i] = 0.95f;
            if (i + 1 < n) kick[i + 1] = 0.55f;
        }

        var strip = new WaveformStrip
        {
            Peaks = peaks,
            KickPeaks = kick,
            Progress = 0.45,
            BarBrush = ahead,
            PlayedBrush = body,
            KickBrush = kickBrush,
        };

        var window = new Window
        {
            Width = 720,
            Height = 84,
            Content = new Border { Background = well, Child = strip },
        };

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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
