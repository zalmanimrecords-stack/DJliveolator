using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// The deck waveform strip. When <see cref="Peaks"/> is set it draws the real track overview (mirrored
/// 0..1 magnitudes from <c>WaveformBuilder</c>) with a <see cref="Progress"/> playhead, a played/unplayed
/// split, and an optional <see cref="BeatGrid"/> overlay; with no peaks it falls back to a deterministic
/// decorative graphic (the "no track loaded" placeholder). Clicking the strip computes the clicked 0..1
/// track fraction and invokes <see cref="SeekCommand"/> with it (the deck VM turns that into a DeckSeek
/// action). Purely presentational — the peak/grid data and the seek behaviour come from the view-model.
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

    /// <summary>Brush for the beat-grid lines (a faint hairline behind the waveform).</summary>
    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x40, 0xE8, 0xEE, 0xF6)));

    /// <summary>The waveform overview peaks (each 0..1), or null/empty to draw the placeholder.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(Peaks));

    /// <summary>Beat-line positions as 0..1 track fractions to overlay; null/empty draws no grid.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> BeatGridProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<double>?>(nameof(BeatGrid));

    /// <summary>Playhead position as a 0..1 fraction of the track.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformStrip, double>(nameof(Progress));

    /// <summary>Invoked on click with the clicked 0..1 track fraction (click-to-seek).</summary>
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformStrip, ICommand?>(nameof(SeekCommand));

    static WaveformStrip()
    {
        AffectsRender<WaveformStrip>(
            BarBrushProperty, PlayedBrushProperty, GridBrushProperty,
            PeaksProperty, BeatGridProperty, ProgressProperty);
    }

    public WaveformStrip()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush PlayedBrush { get => GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public IReadOnlyList<double>? BeatGrid { get => GetValue(BeatGridProperty); set => SetValue(BeatGridProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    /// <summary>
    /// Maps a clicked X coordinate to a 0..1 track fraction. Pure so it unit-tests without a render: a
    /// non-positive width yields 0; the result is clamped to 0..1.
    /// </summary>
    public static double FractionFromX(double x, double width)
    {
        if (width <= 0 || double.IsNaN(x) || double.IsNaN(width))
            return 0;
        return Math.Clamp(x / width, 0.0, 1.0);
    }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        RenderBeatGrid(context, b);

        IReadOnlyList<float>? peaks = Peaks;
        if (peaks is { Count: > 0 })
            RenderWaveform(context, b, peaks);
        else
            RenderPlaceholder(context, b);
    }

    // Faint vertical lines at each beat fraction, drawn behind the waveform so they read as a guide.
    private void RenderBeatGrid(DrawingContext context, Rect b)
    {
        IReadOnlyList<double>? grid = BeatGrid;
        if (grid is not { Count: > 0 })
            return;

        var pen = new Pen(GridBrush, 1);
        foreach (double fraction in grid)
        {
            if (fraction < 0 || fraction > 1)
                continue;
            double x = fraction * b.Width;
            context.DrawLine(pen, new Point(x, 0), new Point(x, b.Height));
        }
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

    // Click-to-seek: only when a track is loaded (peaks present) and a command is bound, so clicking the
    // empty placeholder does nothing.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ICommand? command = SeekCommand;
        if (command is null || Peaks is not { Count: > 0 })
            return;

        double fraction = FractionFromX(e.GetPosition(this).X, Bounds.Width);
        if (command.CanExecute(fraction))
            command.Execute(fraction);
        Focus();
        e.Handled = true;
    }
}
