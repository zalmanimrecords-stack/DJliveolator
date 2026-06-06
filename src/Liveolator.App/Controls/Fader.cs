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

    private bool _dragging;
    private double _dragStart;
    private double _dragStartValue;

    static Fader()
    {
        AffectsRender<Fader>(ValueProperty, OrientationProperty, TrackBrushProperty, FillBrushProperty, ThumbBrushProperty, IsEnabledProperty);
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

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.FromRgb(0x07, 0x0A, 0x0F));
    private static readonly IBrush CapRim = new SolidColorBrush(Color.FromRgb(0x33, 0x3F, 0x52));
    private const int TickCount = 9;

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        double value = Math.Clamp(Value, 0, 1);
        bool on = IsEnabled;
        IBrush fill = on ? FillBrush : TrackBrush;
        IBrush centreLine = on ? FillBrush : TrackBrush;
        const double trackWidth = 4;
        const double pad = 10;
        var tickPen = new Pen(TrackBrush, 1.5) { LineCap = PenLineCap.Round };

        if (Orientation == Orientation.Vertical)
        {
            double cx = b.Width / 2;
            double top = pad, bottom = b.Height - pad;
            double len = Math.Max(1, bottom - top);
            double thumbY = bottom - (value * len);

            // dB tick marks flanking the track
            for (int i = 0; i < TickCount; i++)
            {
                double y = top + (len * i / (TickCount - 1));
                context.DrawLine(tickPen, new Point(cx - 11, y), new Point(cx - 6, y));
                context.DrawLine(tickPen, new Point(cx + 6, y), new Point(cx + 11, y));
            }

            context.DrawLine(new Pen(SlotBrush, trackWidth + 5) { LineCap = PenLineCap.Round }, new Point(cx, top), new Point(cx, bottom));
            context.DrawLine(new Pen(TrackBrush, trackWidth) { LineCap = PenLineCap.Round }, new Point(cx, top), new Point(cx, bottom));
            context.DrawLine(new Pen(fill, trackWidth) { LineCap = PenLineCap.Round }, new Point(cx, thumbY), new Point(cx, bottom));

            double capW = Math.Min(b.Width, 30);
            var cap = new Rect(cx - (capW / 2), thumbY - 8, capW, 16);
            context.DrawRectangle(CapGradient(), new Pen(CapRim, 1), cap, 4, 4);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xD7, 0xE0, 0xEA)), 1),
                new Point(cap.Left + 4, cap.Top + 3), new Point(cap.Right - 4, cap.Top + 3));
            context.DrawLine(new Pen(centreLine, 2) { LineCap = PenLineCap.Round },
                new Point(cap.Left + 4, thumbY), new Point(cap.Right - 4, thumbY));
        }
        else
        {
            double cy = b.Height / 2;
            double left = pad, right = b.Width - pad;
            double len = Math.Max(1, right - left);
            double thumbX = left + (value * len);

            context.DrawLine(new Pen(SlotBrush, trackWidth + 5) { LineCap = PenLineCap.Round }, new Point(left, cy), new Point(right, cy));
            context.DrawLine(new Pen(TrackBrush, trackWidth) { LineCap = PenLineCap.Round }, new Point(left, cy), new Point(right, cy));
            context.DrawLine(new Pen(fill, trackWidth) { LineCap = PenLineCap.Round }, new Point(left, cy), new Point(thumbX, cy));

            double capH = Math.Min(b.Height, 30);
            var cap = new Rect(thumbX - 8, cy - (capH / 2), 16, capH);
            context.DrawRectangle(CapGradient(), new Pen(CapRim, 1), cap, 4, 4);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xD7, 0xE0, 0xEA)), 1),
                new Point(cap.Left + 3, cap.Top + 4), new Point(cap.Left + 3, cap.Bottom - 4));
            context.DrawLine(new Pen(centreLine, 2) { LineCap = PenLineCap.Round },
                new Point(thumbX, cap.Top + 4), new Point(thumbX, cap.Bottom - 4));
        }
    }

    private static IBrush CapGradient()
        => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x3B, 0x47, 0x58), 0),
                new GradientStop(Color.FromRgb(0x20, 0x29, 0x36), 0.48),
                new GradientStop(Color.FromRgb(0x10, 0x15, 0x1D), 1),
            },
        };

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
