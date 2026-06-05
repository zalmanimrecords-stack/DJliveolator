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

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Fader, double>(
            nameof(Value), defaultValue: 0.5,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceUnit);

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
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        double value = Math.Clamp(Value, 0, 1);
        bool on = IsEnabled;
        IBrush fill = on ? FillBrush : TrackBrush;
        IBrush thumb = on ? ThumbBrush : TrackBrush;
        const double trackWidth = 4;
        const double pad = 8;

        if (Orientation == Orientation.Vertical)
        {
            double cx = b.Width / 2;
            double top = pad, bottom = b.Height - pad;
            double len = Math.Max(1, bottom - top);
            double thumbY = bottom - (value * len);
            var trackPen = new Pen(TrackBrush, trackWidth) { LineCap = PenLineCap.Round };
            context.DrawLine(trackPen, new Point(cx, top), new Point(cx, bottom));
            var fillPen = new Pen(fill, trackWidth) { LineCap = PenLineCap.Round };
            context.DrawLine(fillPen, new Point(cx, thumbY), new Point(cx, bottom));
            double capW = Math.Min(b.Width, 26);
            var cap = new Rect(cx - (capW / 2), thumbY - 6, capW, 12);
            context.DrawRectangle(thumb, null, cap, 3, 3);
        }
        else
        {
            double cy = b.Height / 2;
            double left = pad, right = b.Width - pad;
            double len = Math.Max(1, right - left);
            double thumbX = left + (value * len);
            var trackPen = new Pen(TrackBrush, trackWidth) { LineCap = PenLineCap.Round };
            context.DrawLine(trackPen, new Point(left, cy), new Point(right, cy));
            var fillPen = new Pen(fill, trackWidth) { LineCap = PenLineCap.Round };
            context.DrawLine(fillPen, new Point(left, cy), new Point(thumbX, cy));
            double capH = Math.Min(b.Height, 26);
            var cap = new Rect(thumbX - 6, cy - (capH / 2), 12, capH);
            context.DrawRectangle(thumb, null, cap, 3, 3);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
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
        double len = Math.Max(1, (Orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width) - 16);
        double delta = Orientation == Orientation.Vertical
            ? _dragStart - p.Y      // up = increase
            : p.X - _dragStart;     // right = increase
        Value = Math.Clamp(_dragStartValue + (delta / len), 0, 1);
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
