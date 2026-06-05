using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A rotary knob bound to a normalized 0..1 <see cref="Value"/> (two-way). It draws a 270° track arc
/// with an accent value arc + pointer, and is changed by a vertical drag (up = increase) or the arrow
/// keys — emitting through the bound view-model exactly like a slider. Purely presentation: the value
/// it writes flows out via the binding, so the action layer (doc 04) is unchanged. Disabled knobs render
/// in the neutral track color and ignore input.
/// </summary>
public sealed class Knob : Control
{
    /// <summary>Pixels of vertical drag that span the full 0..1 range.</summary>
    private const double DragRangePixels = 160.0;
    private const double KeyStep = 0.05;
    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Knob, double>(
            nameof(Value), defaultValue: 0.5,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceUnit);

    public static readonly StyledProperty<IBrush> ArcBrushProperty =
        AvaloniaProperty.Register<Knob, IBrush>(nameof(ArcBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<Knob, IBrush>(nameof(TrackBrush), new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x40)));

    public static readonly StyledProperty<IBrush> PointerBrushProperty =
        AvaloniaProperty.Register<Knob, IBrush>(nameof(PointerBrush), new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)));

    public static readonly StyledProperty<IBrush> CapBrushProperty =
        AvaloniaProperty.Register<Knob, IBrush>(nameof(CapBrush), new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x1F)));

    private bool _dragging;
    private double _dragStartY;
    private double _dragStartValue;

    static Knob()
    {
        AffectsRender<Knob>(ValueProperty, ArcBrushProperty, TrackBrushProperty, PointerBrushProperty, CapBrushProperty, IsEnabledProperty);
    }

    public Knob()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = 56;
        Height = 56;
    }

    /// <summary>Normalized value, 0..1 (two-way bound).</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }
    public IBrush CapBrush { get => GetValue(CapBrushProperty); set => SetValue(CapBrushProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        double size = Math.Min(b.Width, b.Height);
        if (size <= 0)
            return;

        double stroke = Math.Max(2.5, size * 0.06);
        double radius = (size / 2) - stroke;
        var centre = new Point(b.Width / 2, b.Height / 2);
        double value = Math.Clamp(Value, 0, 1);
        bool on = IsEnabled;

        // track (full 270° sweep)
        var trackPen = new Pen(TrackBrush, stroke) { LineCap = PenLineCap.Round };
        context.DrawGeometry(null, trackPen, Arc(centre, radius, StartAngle, StartAngle + SweepAngle));

        // value arc
        if (value > 0.001)
        {
            var valuePen = new Pen(on ? ArcBrush : TrackBrush, stroke) { LineCap = PenLineCap.Round };
            context.DrawGeometry(null, valuePen, Arc(centre, radius, StartAngle, StartAngle + (SweepAngle * value)));
        }

        // centre cap
        context.DrawEllipse(CapBrush, null, centre, radius * 0.62, radius * 0.62);

        // pointer
        double angle = StartAngle + (SweepAngle * value);
        var inner = PointOnCircle(centre, radius * 0.30, angle);
        var outer = PointOnCircle(centre, radius * 0.90, angle);
        var pointerPen = new Pen(on ? PointerBrush : TrackBrush, Math.Max(2, stroke * 0.8)) { LineCap = PenLineCap.Round };
        context.DrawLine(pointerPen, inner, outer);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        _dragging = true;
        _dragStartY = e.GetPosition(this).Y;
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
        double dy = _dragStartY - e.GetPosition(this).Y; // up = increase
        Value = Math.Clamp(_dragStartValue + (dy / DragRangePixels), 0, 1);
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

    private static Point PointOnCircle(Point centre, double radius, double angleDegrees)
    {
        double a = angleDegrees * Math.PI / 180.0;
        return new Point(centre.X + (radius * Math.Cos(a)), centre.Y + (radius * Math.Sin(a)));
    }

    private static StreamGeometry Arc(Point centre, double radius, double startDegrees, double endDegrees)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            Point start = PointOnCircle(centre, radius, startDegrees);
            Point end = PointOnCircle(centre, radius, endDegrees);
            bool largeArc = (endDegrees - startDegrees) > 180.0;
            ctx.BeginFigure(start, isFilled: false);
            ctx.ArcTo(end, new Size(radius, radius), rotationAngle: 0, isLargeArc: largeArc, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geometry;
    }
}
