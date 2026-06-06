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
        Rect b = Bounds;
        double size = Math.Min(b.Width, b.Height);
        if (size <= 0)
            return;

        bool on = IsEnabled;
        double value = Math.Clamp(Value, 0, 1);
        var centre = new Point(b.Width / 2, b.Height / 2);
        double arcStroke = Math.Max(2.5, size * 0.055);
        double arcRadius = (size / 2) - (arcStroke / 2) - 1;
        double bodyRadius = arcRadius - (arcStroke / 2) - size * 0.06;
        IBrush arc = on ? ArcBrush : TrackBrush;

        // soft cast shadow under the cap — a radial dark→clear ellipse nudged down so the knob reads as
        // a physical cap floating above the panel. DrawingContext has no blur filter, so the radial
        // falloff fakes the penumbra.
        var castShadow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x66, 0, 0, 0), 0.58),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0),
            },
        };
        double shadowRadius = bodyRadius * 1.18;
        context.DrawEllipse(castShadow, null, new Point(centre.X, centre.Y + size * 0.055), shadowRadius, shadowRadius);

        // recessed track ring
        var trackPen = new Pen(TrackBrush, arcStroke) { LineCap = PenLineCap.Round };
        context.DrawGeometry(null, trackPen, Arc(centre, arcRadius, StartAngle, StartAngle + SweepAngle));

        // value arc — a soft wide glow behind a crisp arc
        double angle = StartAngle + (SweepAngle * value);
        if (value > 0.004)
        {
            if (on)
            {
                var glowPen = new Pen(Halo(arc, 0.22), arcStroke * 2.4) { LineCap = PenLineCap.Round };
                context.DrawGeometry(null, glowPen, Arc(centre, arcRadius, StartAngle, angle));
            }
            var valuePen = new Pen(arc, arcStroke) { LineCap = PenLineCap.Round };
            context.DrawGeometry(null, valuePen, Arc(centre, arcRadius, StartAngle, angle));
        }

        // unity / home mark — a faint notch across the track at DefaultValue so "flat" is findable at a glance
        double detentAngle = StartAngle + (SweepAngle * Math.Clamp(DefaultValue, 0, 1));
        var detentPen = new Pen(Halo(PointerBrush, 0.5), 1.5) { LineCap = PenLineCap.Round };
        context.DrawLine(detentPen,
            PointOnCircle(centre, arcRadius - (arcStroke * 0.5), detentAngle),
            PointOnCircle(centre, arcRadius + (arcStroke * 0.5), detentAngle));

        // knob body — radial gradient for a soft 3-D cap with the light pooled toward the top, drawn
        // with no outline so the bevel rim below can own the edge.
        var body = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.34, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.26, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.78, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.78, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x27, 0x33, 0x46), 0.0),
                new GradientStop(Color.FromRgb(0x15, 0x1C, 0x28), 0.62),
                new GradientStop(Color.FromRgb(0x09, 0x0C, 0x12), 1.0),
            },
        };
        context.DrawEllipse(body, null, centre, bodyRadius, bodyRadius);

        // bevel rim — a vertical light→dark stroke so the cap edge catches light at the top and falls
        // into shadow at the bottom, reading as a turned rim rather than a flat hairline.
        var rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x3A, 0x46, 0x5A), 0.0),
                new GradientStop(Color.FromRgb(0x12, 0x18, 0x22), 0.5),
                new GradientStop(Color.FromRgb(0x05, 0x07, 0x0B), 1.0),
            },
        };
        context.DrawEllipse(null, new Pen(rim, Math.Max(1.0, size * 0.03)), centre, bodyRadius, bodyRadius);

        // specular highlight — a soft light pool near the top of the cap for a subtle gloss; brighter
        // when enabled so a disabled knob stays matte.
        var spec = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(on ? (byte)0x3A : (byte)0x20, 0xE6, 0xEE, 0xF8), 0.0),
                new GradientStop(Color.FromArgb(0x00, 0xE6, 0xEE, 0xF8), 1.0),
            },
        };
        context.DrawEllipse(spec, null,
            new Point(centre.X, centre.Y - bodyRadius * 0.36), bodyRadius * 0.66, bodyRadius * 0.44);

        // glow dot at the value position (halo + core), then a short pointer tick on the body
        if (on)
        {
            Point dot = PointOnCircle(centre, arcRadius, angle);
            context.DrawEllipse(Halo(arc, 0.35), null, dot, arcStroke * 1.6, arcStroke * 1.6);
            context.DrawEllipse(arc, null, dot, arcStroke * 0.7, arcStroke * 0.7);
        }

        var tickInner = PointOnCircle(centre, bodyRadius * 0.36, angle);
        var tickOuter = PointOnCircle(centre, bodyRadius * 0.86, angle);
        var pointerPen = new Pen(on ? PointerBrush : TrackBrush, Math.Max(2, arcStroke * 0.7)) { LineCap = PenLineCap.Round };
        context.DrawLine(pointerPen, tickInner, tickOuter);
    }

    private static IBrush Halo(IBrush source, double opacity)
    {
        if (source is ISolidColorBrush s)
        {
            Color c = s.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
        }
        return source;
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
