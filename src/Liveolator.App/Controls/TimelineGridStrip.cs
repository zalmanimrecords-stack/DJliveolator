using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Liveolator.App.Controls;

/// <summary>
/// A non-interactive STUDIO timeline overlay that draws the project's bar/beat grid: bright vertical lines
/// on each bar (with a bar number), fainter lines on the in-between beats. The grid makes beat-snapping and
/// "sync to project BPM" visible — clips read against the bars they lock to. Lines map time→x with the same
/// <see cref="TimelineMath"/> zoom the clips use, so they stay aligned at every zoom level. Drawing collapses
/// gracefully as it zooms out (beats, then bars, drop out when they would be too dense to read).
/// </summary>
public sealed class TimelineGridStrip : Control
{
    // Below these pixel spacings a line tier is too dense to read, so it is skipped.
    private const double MinBeatSpacingPx = 7;
    private const double MinBarSpacingPx = 4;
    // A bar number is only drawn when bars are at least this far apart (else the labels overlap).
    private const double MinBarLabelSpacingPx = 26;

    public static readonly StyledProperty<double> PixelsPerSecondProperty =
        AvaloniaProperty.Register<TimelineGridStrip, double>(nameof(PixelsPerSecond), 8.0);

    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<TimelineGridStrip, double>(nameof(Bpm), 120.0);

    public static readonly StyledProperty<int> BeatsPerBarProperty =
        AvaloniaProperty.Register<TimelineGridStrip, int>(nameof(BeatsPerBar), 4);

    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<TimelineGridStrip, IBrush>(
            nameof(BarBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0xE8, 0xEE, 0xF6)));

    public static readonly StyledProperty<IBrush> BeatBrushProperty =
        AvaloniaProperty.Register<TimelineGridStrip, IBrush>(
            nameof(BeatBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x1E, 0xE8, 0xEE, 0xF6)));

    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<TimelineGridStrip, IBrush>(
            nameof(LabelBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xE8, 0xEE, 0xF6)));

    static TimelineGridStrip()
    {
        AffectsRender<TimelineGridStrip>(
            PixelsPerSecondProperty, BpmProperty, BeatsPerBarProperty,
            BarBrushProperty, BeatBrushProperty, LabelBrushProperty);
    }

    public TimelineGridStrip() => IsHitTestVisible = false;

    public double PixelsPerSecond { get => GetValue(PixelsPerSecondProperty); set => SetValue(PixelsPerSecondProperty, value); }
    public double Bpm { get => GetValue(BpmProperty); set => SetValue(BpmProperty, value); }
    public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush BeatBrush { get => GetValue(BeatBrushProperty); set => SetValue(BeatBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0 || Bpm <= 0 || PixelsPerSecond <= 0)
            return;

        int beatsPerBar = BeatsPerBar > 0 ? BeatsPerBar : 4;
        double beatPx = (60.0 / Bpm) * PixelsPerSecond;
        double barPx = beatPx * beatsPerBar;
        if (beatPx <= 0 || barPx < MinBarSpacingPx)
            return; // zoomed too far out for even bars to read

        bool drawBeats = beatPx >= MinBeatSpacingPx;
        bool drawLabels = barPx >= MinBarLabelSpacingPx;
        var barPen = new Pen(BarBrush, 1);
        var beatPen = new Pen(BeatBrush, 1);

        for (int beat = 0; ; beat++)
        {
            double x = beat * beatPx;
            if (x > b.Width)
                break;

            if (beat % beatsPerBar == 0)
            {
                context.DrawLine(barPen, new Point(x, 0), new Point(x, b.Height));
                if (drawLabels)
                    DrawBarNumber(context, x, beat / beatsPerBar + 1); // bars are 1-based for the user
            }
            else if (drawBeats)
            {
                context.DrawLine(beatPen, new Point(x, 0), new Point(x, b.Height));
            }
        }
    }

    private void DrawBarNumber(DrawingContext context, double x, int barNumber)
    {
        var text = new FormattedText(
            barNumber.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            LabelBrush);
        context.DrawText(text, new Point(x + 2, 1));
    }
}
