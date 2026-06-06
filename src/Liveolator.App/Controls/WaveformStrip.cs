using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// The deck waveform strip. When <see cref="Peaks"/> is set it draws the real track overview (mirrored
/// 0..1 magnitudes from <c>WaveformBuilder</c>) with a <see cref="Progress"/> playhead and a played/unplayed
/// split; with no peaks it falls back to a deterministic decorative graphic (the "no track loaded"
/// placeholder). Purely presentational — the peak data and playhead come from the deck view-model.
/// </summary>
public sealed class WaveformStrip : Control
{
    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(BarBrush), new SolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x80, 0xF6)));

    /// <summary>Brush for the part of the waveform already played (left of the playhead).</summary>
    public static readonly StyledProperty<IBrush> PlayedBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(PlayedBrush), new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6)));

    /// <summary>The waveform overview peaks (each 0..1), or null/empty to draw the placeholder.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(Peaks));

    /// <summary>Playhead position as a 0..1 fraction of the track.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformStrip, double>(nameof(Progress));

    static WaveformStrip()
    {
        AffectsRender<WaveformStrip>(BarBrushProperty, PlayedBrushProperty, PeaksProperty, ProgressProperty);
    }

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush PlayedBrush { get => GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        IReadOnlyList<float>? peaks = Peaks;
        if (peaks is { Count: > 0 })
            RenderWaveform(context, b, peaks);
        else
            RenderPlaceholder(context, b);
    }

    // Real data: one mirrored bar per column, sampled from the peak buckets; bars left of the playhead
    // use the "played" brush.
    private void RenderWaveform(DrawingContext context, Rect b, IReadOnlyList<float> peaks)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 2;
        const double step = 2.0;
        double playheadX = Math.Clamp(Progress, 0.0, 1.0) * b.Width;

        var playedPen = new Pen(PlayedBrush, 1.5) { LineCap = PenLineCap.Round };
        var aheadPen = new Pen(BarBrush, 1.5) { LineCap = PenLineCap.Round };

        for (double x = 1; x < b.Width - 1; x += step)
        {
            int index = (int)(x / b.Width * peaks.Count);
            if (index < 0) index = 0;
            else if (index >= peaks.Count) index = peaks.Count - 1;

            double amp = maxAmp * Math.Clamp(peaks[index], 0f, 1f);
            if (amp < 0.5) amp = 0.5; // keep a hairline so silent regions still read as a strip
            Pen pen = x <= playheadX ? playedPen : aheadPen;
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }

        if (Progress > 0.0 && Progress < 1.0)
        {
            var headPen = new Pen(PlayedBrush, 1.5);
            context.DrawLine(headPen, new Point(playheadX, 0), new Point(playheadX, b.Height));
        }
    }

    // No track: the original deterministic pseudo-waveform placeholder.
    private void RenderPlaceholder(DrawingContext context, Rect b)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 3;
        const double step = 3.0;
        var pen = new Pen(BarBrush, 2) { LineCap = PenLineCap.Round };

        for (double x = 2; x < b.Width - 2; x += step)
        {
            double env = 0.45 + (0.55 * Math.Abs(Math.Sin(x * 0.018)));
            double shape = (Math.Sin(x * 0.13) * 0.6) + (Math.Sin((x * 0.37) + 1.0) * 0.4);
            double amp = maxAmp * env * (0.25 + (0.75 * Math.Abs(shape)));
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }
    }
}
