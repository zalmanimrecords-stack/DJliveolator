using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A linear fader bound to a normalized 0..1 <see cref="Value"/> (two-way), used for the mixer channel
/// volumes (vertical) and the crossfader (horizontal). Draws a track with an accent fill from the start
/// to the cap, and is changed by dragging the cap (or the arrow keys). Like <see cref="Knob"/> it is pure
/// presentation — the value flows out through the binding to the view-model, so the action layer
/// (doc 04) is unchanged. Disabled faders render neutral and ignore input.
/// </summary>
public sealed class Fader : Control
{
    private const double KeyStep = 0.05;
    /// <summary>Holding Shift slows the drag by this factor for precise level/crossfade trims.</summary>
    private const double FineDragFactor = 5.0;
    private const int TickCount = 9;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Fader, double>(
            nameof(Value), defaultValue: 0.5,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceUnit);

    /// <summary>The "home" value a double-click snaps back to (e.g. crossfader centre). Default 0.5.</summary>
    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(DefaultValue), defaultValue: 0.5, coerce: CoerceUnit);

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Fader, Orientation>(nameof(Orientation), Orientation.Vertical);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush>(nameof(TrackBrush), new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3F)));

    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush>(nameof(FillBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> ThumbBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush>(nameof(ThumbBrush), new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)));

    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Level), defaultValue: 0.0, coerce: CoerceUnit);

    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0D));
    private static readonly IBrush CapRim = new SolidColorBrush(Color.FromRgb(0x33, 0x3F, 0x52));

    private bool _dragging;
    private double _dragStart;
    private double _dragStartValue;

    static Fader()
    {
        AffectsRender<Fader>(
            ValueProperty, LevelProperty, OrientationProperty, TrackBrushProperty,
            FillBrushProperty, ThumbBrushProperty, IsEnabledProperty);
    }

    public Fader()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }
    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double value = Math.Clamp(Value, 0, 1);
        bool on = IsEnabled;
        IBrush fill = on ? FillBrush : TrackBrush;
        IBrush centreLine = on ? FillBrush : TrackBrush;
        const double trackWidth = 4;
        const double pad = 10;

        if (Orientation == Orientation.Vertical)
        {
            double cx = bounds.Width / 2;
            double top = pad;
            double bottom = bounds.Height - pad;
            double length = Math.Max(1, bottom - top);
            double thumbY = bottom - (value * length);

            DrawVerticalLevelMeter(context, cx, top, bottom, Math.Clamp(Level, 0, 1));
            DrawVerticalTicks(context, cx, top, length);
            DrawSlot(context, new Point(cx, top), new Point(cx, bottom), trackWidth, vertical: true);
            DrawIlluminatedFill(context, new Point(cx, thumbY), new Point(cx, bottom), fill, trackWidth);

            double capWidth = Math.Min(bounds.Width, 30);
            var cap = new Rect(cx - (capWidth / 2), thumbY - 8, capWidth, 16);
            DrawCapShadow(context, cap, vertical: true);
            DrawCap(context, cap, centreLine, vertical: true);
        }
        else
        {
            double cy = bounds.Height / 2;
            double left = pad;
            double right = bounds.Width - pad;
            double length = Math.Max(1, right - left);
            double thumbX = left + (value * length);

            DrawHorizontalTicks(context, cy, left, length);
            DrawSlot(context, new Point(left, cy), new Point(right, cy), trackWidth, vertical: false);
            DrawIlluminatedFill(context, new Point(left, cy), new Point(thumbX, cy), fill, trackWidth);

            double capHeight = Math.Min(bounds.Height, 30);
            var cap = new Rect(thumbX - 8, cy - (capHeight / 2), 16, capHeight);
            DrawCapShadow(context, cap, vertical: false);
            DrawCap(context, cap, centreLine, vertical: false);
        }
    }

    private static void DrawVerticalLevelMeter(
        DrawingContext context, double centreX, double top, double bottom, double level)
    {
        const int segments = 16;
        const double width = 5;
        const double gap = 2;
        double height = bottom - top;
        double segmentHeight = Math.Max(1, (height - ((segments - 1) * gap)) / segments);
        double x = centreX + 16;
        int lit = (int)Math.Ceiling(level * segments);

        for (int index = 0; index < segments; index++)
        {
            double y = bottom - ((index + 1) * segmentHeight) - (index * gap);
            var rect = new Rect(x, y, width, segmentHeight);
            IBrush brush = index < lit
                ? MeterBrush(index, segments)
                : new SolidColorBrush(Color.FromArgb(0x70, 0x18, 0x20, 0x29));
            context.DrawRectangle(brush, null, rect, 1, 1);
        }
    }

    private static IBrush MeterBrush(int index, int count)
    {
        double fraction = (double)(index + 1) / count;
        Color color = fraction > 0.875
            ? Color.FromRgb(0xE5, 0x54, 0x4A)
            : fraction > 0.68
                ? Color.FromRgb(0xFF, 0xA2, 0x2B)
                : Color.FromRgb(0x29, 0xC4, 0x67);
        return new SolidColorBrush(color);
    }

    private void DrawSlot(DrawingContext context, Point start, Point end, double width, bool vertical)
    {
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)), width + 8)
            { LineCap = PenLineCap.Round }, start, end);

        Point litOffset = vertical ? new Point(-1.1, 0) : new Point(0, -1.1);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x72, 0x70, 0x7E, 0x90)), width + 4)
            { LineCap = PenLineCap.Round }, start + litOffset, end + litOffset);
        context.DrawLine(new Pen(SlotBrush, width + 3) { LineCap = PenLineCap.Round }, start, end);
        context.DrawLine(new Pen(TrackBrush, width) { LineCap = PenLineCap.Round }, start, end);

        Point shadowOffset = vertical ? new Point(1, 0) : new Point(0, 1);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), 1.2)
            { LineCap = PenLineCap.Round }, start + shadowOffset, end + shadowOffset);
    }

    private static void DrawIlluminatedFill(
        DrawingContext context,
        Point start,
        Point end,
        IBrush fill,
        double width)
    {
        context.DrawLine(new Pen(Halo(fill, 0.2), width * 2.8) { LineCap = PenLineCap.Round }, start, end);
        context.DrawLine(new Pen(Halo(fill, 0.42), width * 1.6) { LineCap = PenLineCap.Round }, start, end);
        context.DrawLine(new Pen(fill, width) { LineCap = PenLineCap.Round }, start, end);
        context.DrawLine(new Pen(Halo(fill, 0.7), 1) { LineCap = PenLineCap.Round },
            start + new Point(-0.55, -0.55), end + new Point(-0.55, -0.55));
    }

    private static void DrawVerticalTicks(DrawingContext context, double cx, double top, double length)
    {
        for (int i = 0; i < TickCount; i++)
        {
            double y = top + (length * i / (TickCount - 1));
            DrawEngravedLine(context, new Point(cx - 12, y), new Point(cx - 6.5, y));
            DrawEngravedLine(context, new Point(cx + 6.5, y), new Point(cx + 12, y));
        }
    }

    private static void DrawHorizontalTicks(DrawingContext context, double cy, double left, double length)
    {
        for (int i = 0; i < TickCount; i++)
        {
            double x = left + (length * i / (TickCount - 1));
            DrawEngravedLine(context, new Point(x, cy - 12), new Point(x, cy - 6.5));
            DrawEngravedLine(context, new Point(x, cy + 6.5), new Point(x, cy + 12));
        }
    }

    private static void DrawEngravedLine(DrawingContext context, Point start, Point end)
    {
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x01, 0x03, 0x07)), 1.5)
            { LineCap = PenLineCap.Round }, start + new Point(0.6, 0.7), end + new Point(0.6, 0.7));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x7C, 0x83, 0x91, 0xA2)), 1)
            { LineCap = PenLineCap.Round }, start, end);
    }

    private static void DrawCapShadow(DrawingContext context, Rect cap, bool vertical)
    {
        // WHY: stepped silhouettes fake the cap-to-slot contact shadow without blur.
        Point offset = vertical ? new Point(0, 2.5) : new Point(2.5, 1);
        var broad = new Rect(cap.X - 3 + offset.X, cap.Y - 3 + offset.Y, cap.Width + 6, cap.Height + 6);
        var tight = new Rect(cap.X - 1 + offset.X, cap.Y - 1 + offset.Y, cap.Width + 2, cap.Height + 2);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0)), null, broad, 6, 6);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x68, 0, 0, 0)), null, tight, 5, 5);
    }

    private void DrawCap(DrawingContext context, Rect cap, IBrush centreLine, bool vertical)
    {
        context.DrawRectangle(ThumbBrush, null, cap, 4, 4);
        context.DrawRectangle(CapGradient(), new Pen(CapRim, 1.2), cap, 4, 4);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x74, 0xE4, 0xEB, 0xF3)), 1),
            new Point(cap.Left + 4, cap.Top + 2.5), new Point(cap.Right - 4, cap.Top + 2.5));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x9A, 0x03, 0x06, 0x0B)), 1),
            new Point(cap.Left + 2.5, cap.Bottom - 2), new Point(cap.Right - 2.5, cap.Bottom - 2));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x5C, 0xA8, 0xB4, 0xC2)), 1),
            new Point(cap.Left + 2, cap.Top + 4), new Point(cap.Left + 2, cap.Bottom - 4));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x02, 0x04, 0x08)), 1.2),
            new Point(cap.Right - 2, cap.Top + 4), new Point(cap.Right - 2, cap.Bottom - 4));

        if (vertical)
        {
            double y = cap.Center.Y;
            DrawGripGroove(context, new Point(cap.Left + 4, y - 3), new Point(cap.Right - 4, y - 3));
            DrawGripGroove(context, new Point(cap.Left + 4, y + 3), new Point(cap.Right - 4, y + 3));
            context.DrawLine(new Pen(centreLine, 2) { LineCap = PenLineCap.Round },
                new Point(cap.Left + 4, y), new Point(cap.Right - 4, y));
        }
        else
        {
            double x = cap.Center.X;
            DrawGripGroove(context, new Point(x - 3, cap.Top + 4), new Point(x - 3, cap.Bottom - 4));
            DrawGripGroove(context, new Point(x + 3, cap.Top + 4), new Point(x + 3, cap.Bottom - 4));
            context.DrawLine(new Pen(centreLine, 2) { LineCap = PenLineCap.Round },
                new Point(x, cap.Top + 4), new Point(x, cap.Bottom - 4));
        }
    }

    private static void DrawGripGroove(DrawingContext context, Point start, Point end)
    {
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x02, 0x04, 0x08)), 1.4)
            { LineCap = PenLineCap.Round }, start, end);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x54, 0xA1, 0xAD, 0xBC)), 0.8)
            { LineCap = PenLineCap.Round }, start + new Point(-0.5, -0.5), end + new Point(-0.5, -0.5));
    }

    private static IBrush CapGradient()
        => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xF0, 0x55, 0x62, 0x73), 0),
                new GradientStop(Color.FromArgb(0xF2, 0x34, 0x3E, 0x4D), 0.2),
                new GradientStop(Color.FromArgb(0xF6, 0x1B, 0x23, 0x2E), 0.58),
                new GradientStop(Color.FromArgb(0xFA, 0x0B, 0x10, 0x17), 0.82),
                new GradientStop(Color.FromArgb(0xF0, 0x21, 0x2A, 0x36), 1),
            },
        };

    private static IBrush Halo(IBrush source, double opacity)
    {
        if (source is ISolidColorBrush solid)
        {
            Color color = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        }

        return source;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        // Double-click snaps to the home value (e.g. crossfader centre).
        if (e.ClickCount >= 2)
        {
            Value = DefaultValue;
            e.Handled = true;
            return;
        }
        _dragging = true;
        Point p = e.GetPosition(this);
        _dragStart = Orientation == Orientation.Vertical ? p.Y : p.X;
        _dragStartValue = Value;
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        Point p = e.GetPosition(this);
        double span = Math.Max(1, (Orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width) - 16);
        double range = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? span * FineDragFactor : span;
        double delta = Orientation == Orientation.Vertical
            ? _dragStart - p.Y      // up = increase
            : p.X - _dragStart;     // right = increase
        Value = Math.Clamp(_dragStartValue + (delta / range), 0, 1);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled)
            return;
        if (e.Key is Key.Up or Key.Right)
        {
            Value = Math.Clamp(Value + KeyStep, 0, 1);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Left)
        {
            Value = Math.Clamp(Value - KeyStep, 0, 1);
            e.Handled = true;
        }
    }
}
