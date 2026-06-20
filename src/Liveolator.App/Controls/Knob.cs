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
public class Knob : Control
{
    /// <summary>Pixels of vertical drag that span the full 0..1 range.</summary>
    private const double DragRangePixels = 160.0;
    /// <summary>Holding Shift slows the drag by this factor for precise EQ/filter trims.</summary>
    private const double FineDragFactor = 5.0;
    private const double KeyStep = 0.05;
    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Knob, double>(
            nameof(Value), defaultValue: 0.5,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceUnit);

    /// <summary>The "home" value: a faint unity mark is drawn here and a double-click snaps back to it
    /// (EQ/filter centre = flat). Default 0.5.</summary>
    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<Knob, double>(nameof(DefaultValue), defaultValue: 0.5, coerce: CoerceUnit);

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
        AffectsRender<Knob>(ValueProperty, DefaultValueProperty, ArcBrushProperty, TrackBrushProperty, PointerBrushProperty, CapBrushProperty, IsEnabledProperty);
    }

    public Knob()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = 53.8;
        Height = 53.8;
    }

    /// <summary>Normalized value, 0..1 (two-way bound).</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The unity / home value (double-click snaps here; a faint mark is drawn at it).</summary>
    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }
    public IBrush CapBrush { get => GetValue(CapBrushProperty); set => SetValue(CapBrushProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
            return;

        bool on = IsEnabled;
        double value = Math.Clamp(Value, 0, 1);
        var centre = new Point(bounds.Width / 2, bounds.Height / 2);
        double arcStroke = Math.Max(2.5, size * 0.055);
        double arcRadius = (size / 2) - (arcStroke / 2) - 1;
        double bodyRadius = arcRadius - (arcStroke / 2) - size * 0.06;
        IBrush arc = on ? ArcBrush : TrackBrush;

        DrawCastShadow(context, centre, bodyRadius, size);
        DrawTrackChannel(context, centre, arcRadius, arcStroke);

        double angle = StartAngle + (SweepAngle * value);
        if (value > 0.004)
        {
            if (on)
            {
                context.DrawGeometry(null, new Pen(ControlBrush.Halo(arc, 0.22), arcStroke * 2.4)
                    { LineCap = PenLineCap.Round }, Arc(centre, arcRadius, StartAngle, angle));
            }

            context.DrawGeometry(null, new Pen(arc, arcStroke)
                { LineCap = PenLineCap.Round }, Arc(centre, arcRadius, StartAngle, angle));
        }

        double detentAngle = StartAngle + (SweepAngle * Math.Clamp(DefaultValue, 0, 1));
        context.DrawLine(new Pen(ControlBrush.Halo(PointerBrush, 0.5), 1.5) { LineCap = PenLineCap.Round },
            PointOnCircle(centre, arcRadius - (arcStroke * 0.5), detentAngle),
            PointOnCircle(centre, arcRadius + (arcStroke * 0.5), detentAngle));

        DrawCap(context, centre, bodyRadius, size, on);
        DrawKnurling(context, centre, bodyRadius, size);

        if (on)
        {
            Point dot = PointOnCircle(centre, arcRadius, angle);
            context.DrawEllipse(ControlBrush.Halo(arc, 0.35), null, dot, arcStroke * 1.6, arcStroke * 1.6);
            context.DrawEllipse(arc, null, dot, arcStroke * 0.7, arcStroke * 0.7);
        }

        Point tickInner = PointOnCircle(centre, bodyRadius * 0.36, angle);
        Point tickOuter = PointOnCircle(centre, bodyRadius * 0.86, angle);
        IBrush pointer = on ? PointerBrush : TrackBrush;
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0x02, 0x04, 0x08)),
            Math.Max(3.2, arcStroke * 1.05)) { LineCap = PenLineCap.Round }, tickInner, tickOuter);
        Point highlightOffset = PointerHighlightOffset(angle);
        context.DrawLine(new Pen(ControlBrush.Halo(pointer, 0.42), Math.Max(1, arcStroke * 0.35))
            { LineCap = PenLineCap.Round }, tickInner + highlightOffset, tickOuter + highlightOffset);
        context.DrawLine(new Pen(pointer, Math.Max(1.5, arcStroke * 0.55))
            { LineCap = PenLineCap.Round }, tickInner, tickOuter);
    }

    private void DrawTrackChannel(DrawingContext context, Point centre, double radius, double stroke)
    {
        StreamGeometry track = Arc(centre, radius, StartAngle, StartAngle + SweepAngle);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0x02, 0x05, 0x0A)),
            stroke + 4.5) { LineCap = PenLineCap.Round }, track);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3B, 0x47, 0x58)),
            stroke + 1.8) { LineCap = PenLineCap.Round }, Arc(new Point(centre.X - 0.45, centre.Y - 0.65),
                radius, StartAngle, StartAngle + SweepAngle));
        context.DrawGeometry(null, new Pen(TrackBrush, stroke) { LineCap = PenLineCap.Round }, track);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x72, 0x00, 0x02, 0x06)),
            Math.Max(1, stroke * 0.34)) { LineCap = PenLineCap.Round }, Arc(new Point(centre.X + 0.4, centre.Y + 0.55),
                radius, StartAngle, StartAngle + SweepAngle));
    }

    private static void DrawCastShadow(DrawingContext context, Point centre, double bodyRadius, double size)
    {
        // WHY: radial falloff substitutes for a blurred contact shadow in DrawingContext.
        var castShadow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x78, 0, 0, 0), 0.54),
                new GradientStop(Color.FromArgb(0x20, 0, 0, 0), 0.78),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
            },
        };
        double shadowRadius = bodyRadius * 1.22;
        context.DrawEllipse(castShadow, null, new Point(centre.X, centre.Y + size * 0.06), shadowRadius, shadowRadius);

        // WHY: the dark ring is ambient occlusion where the cap nearly meets the panel.
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x82, 0x00, 0x02, 0x06)),
            Math.Max(2, size * 0.045)), centre, bodyRadius * 1.07, bodyRadius * 1.07);
    }

    private void DrawCap(DrawingContext context, Point centre, double radius, double size, bool on)
    {
        context.DrawEllipse(CapBrush, null, centre, radius, radius);
        var body = new RadialGradientBrush
        {
            Center = new RelativePoint(0.43, 0.39, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.28, 0.2, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.86, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.86, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xEA, 0x3D, 0x49, 0x5A), 0),
                new GradientStop(Color.FromArgb(0xE8, 0x20, 0x29, 0x36), 0.34),
                new GradientStop(Color.FromArgb(0xF2, 0x0B, 0x0F, 0x16), 0.72),
                new GradientStop(Color.FromArgb(0xE8, 0x1B, 0x24, 0x31), 0.91),
                new GradientStop(Color.FromArgb(0xF8, 0x06, 0x08, 0x0C), 1),
            },
        };
        context.DrawEllipse(body, null, centre, radius, radius);

        var rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x68, 0x76, 0x88), 0),
                new GradientStop(Color.FromRgb(0x2B, 0x36, 0x45), 0.22),
                new GradientStop(Color.FromRgb(0x0C, 0x11, 0x19), 0.58),
                new GradientStop(Color.FromRgb(0x02, 0x04, 0x08), 1),
            },
        };
        context.DrawEllipse(null, new Pen(rim, Math.Max(1.4, size * 0.04)), centre, radius, radius);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x02, 0x06)),
            Math.Max(1, size * 0.018)), centre, radius * 0.89, radius * 0.89);

        var topLight = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(on ? (byte)0x42 : (byte)0x25, 0xE7, 0xEE, 0xF7), 0),
                new GradientStop(Color.FromArgb(0x00, 0xE7, 0xEE, 0xF7), 1),
            },
        };
        context.DrawEllipse(topLight, null, new Point(centre.X - radius * 0.22, centre.Y - radius * 0.38),
            radius * 0.72, radius * 0.43);

        var bounce = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0x78, 0x8A, 0xA2), 0),
                new GradientStop(Color.FromArgb(0x2C, 0x78, 0x8A, 0xA2), 1),
            },
        };
        context.DrawEllipse(bounce, null, new Point(centre.X, centre.Y + radius * 0.47),
            radius * 0.68, radius * 0.27);
    }

    private static void DrawKnurling(DrawingContext context, Point centre, double radius, double size)
    {
        double inner = radius * 0.84;
        double outer = radius * 0.98;
        double width = Math.Max(0.55, size * 0.012);
        var darkPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x02, 0x06)), width);
        var lightPen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x9B, 0xA8, 0xB8)), width);

        for (int i = 0; i < 28; i++)
        {
            double angle = i * (360.0 / 28);
            context.DrawLine(darkPen, PointOnCircle(centre, inner, angle + 1.2),
                PointOnCircle(centre, outer, angle + 1.2));
            context.DrawLine(lightPen, PointOnCircle(centre, inner, angle - 0.8),
                PointOnCircle(centre, outer, angle - 0.8));
        }
    }

    private static Point PointerHighlightOffset(double angleDegrees)
    {
        double angle = angleDegrees * Math.PI / 180.0;
        return new Point(Math.Sin(angle) * 0.75, -Math.Cos(angle) * 0.75);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        // Double-click snaps to the home/unity value (e.g. flat EQ) — the standard DJ-gear reset gesture.
        if (e.ClickCount >= 2)
        {
            Value = DefaultValue;
            e.Handled = true;
            return;
        }
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
        double range = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragRangePixels * FineDragFactor : DragRangePixels;
        Value = Math.Clamp(_dragStartValue + (dy / range), 0, 1);
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
        double angle = angleDegrees * Math.PI / 180.0;
        return new Point(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));
    }

    private static StreamGeometry Arc(Point centre, double radius, double startDegrees, double endDegrees)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            Point start = PointOnCircle(centre, radius, startDegrees);
            Point end = PointOnCircle(centre, radius, endDegrees);
            bool largeArc = (endDegrees - startDegrees) > 180.0;
            context.BeginFigure(start, isFilled: false);
            context.ArcTo(end, new Size(radius, radius), rotationAngle: 0, isLargeArc: largeArc, SweepDirection.Clockwise);
            context.EndFigure(false);
        }
        return geometry;
    }
}
