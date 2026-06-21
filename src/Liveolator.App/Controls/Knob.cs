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

    /// <summary>Number of evenly spaced detents the knob snaps to (e.g. 3 = a 3-position selector at
    /// 0 / 0.5 / 1). 0 (default) or 1 leaves the knob continuous. Arrow keys then step one detent.</summary>
    public static readonly StyledProperty<int> DetentsProperty =
        AvaloniaProperty.Register<Knob, int>(nameof(Detents), defaultValue: 0);

    /// <summary>The drawn shape, set per UI theme via the <c>KnobStyle</c> resource. Defaults to the
    /// skeuomorphic rotary look; the Retro Sci-Fi theme switches it to the chicken-head amp knob.</summary>
    public static readonly StyledProperty<KnobStyle> VariantProperty =
        AvaloniaProperty.Register<Knob, KnobStyle>(nameof(Variant), defaultValue: KnobStyle.Rotary);

    private bool _dragging;
    private double _dragStartY;
    private double _dragStartValue;

    static Knob()
    {
        AffectsRender<Knob>(ValueProperty, DefaultValueProperty, ArcBrushProperty, TrackBrushProperty, PointerBrushProperty, CapBrushProperty, IsEnabledProperty, DetentsProperty, VariantProperty);
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

    /// <summary>Detent count: 0/1 = continuous, N≥2 = snap to N evenly spaced positions.</summary>
    public int Detents { get => GetValue(DetentsProperty); set => SetValue(DetentsProperty, value); }

    /// <summary>The drawn knob shape (theme-selected). Behaviour is identical across styles.</summary>
    public KnobStyle Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    private static double CoerceUnit(AvaloniaObject sender, double value)
    {
        if (double.IsNaN(value))
            value = 0;
        value = Math.Clamp(value, 0.0, 1.0);
        // Snap to the nearest detent so drag, keyboard, and binding write-backs all land on a position.
        if (sender is Knob { Detents: >= 2 } knob)
        {
            int steps = knob.Detents - 1;
            value = Math.Round(value * steps, MidpointRounding.AwayFromZero) / steps;
        }
        return value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Detents may be applied after Value (e.g. XAML attribute order), so re-snap when it changes.
        if (change.Property == DetentsProperty)
        {
            CoerceValue(ValueProperty);
            CoerceValue(DefaultValueProperty);
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
            return;

        bool on = IsEnabled;
        double value = Math.Clamp(Value, 0, 1);
        var centre = new Point(bounds.Width / 2, bounds.Height / 2);

        if (Variant == KnobStyle.ScallopedDial)
        {
            RenderScallopedDial(context, centre, size, value, on);
            return;
        }

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

    // Vintage cream-bakelite amp knob (Retro Sci-Fi theme), modelled on the owner's reference photo:
    // a glossy scalloped (piecrust-edged) cap on a numbered dial plate with engraved sepia ticks/numbers
    // and a brass pointer. Theme brushes drive every colour — Track = dial plate, Arc = engraved ink,
    // Cap = bakelite cream, Pointer = brass — so it stays fully themable. Same StartAngle/SweepAngle sweep
    // as the rotary look, so the value reads identically.
    private const int ScallopBumps = 18;

    private void RenderScallopedDial(DrawingContext context, Point centre, double size, double value, bool on)
    {
        double plateR = (size / 2) - 1;
        double capMid = size * 0.33;
        double scallopAmp = size * 0.026;
        double angle = StartAngle + (SweepAngle * value);

        IBrush plate = on ? TrackBrush : ControlBrush.Halo(TrackBrush, 0.5);
        IBrush cap = on ? CapBrush : ControlBrush.Halo(CapBrush, 0.6);
        IBrush ink = on ? ArcBrush : ControlBrush.Halo(ArcBrush, 0.5);
        IBrush brass = on ? PointerBrush : ControlBrush.Halo(PointerBrush, 0.5);

        DrawCastShadow(context, centre, plateR * 0.96, size);

        // Dial plate: flat disc the engraved scale sits on, with a thin engraved rim.
        context.DrawEllipse(plate, new Pen(ControlBrush.Halo(ink, 0.35), Math.Max(1, size * 0.012)),
            centre, plateR, plateR);
        DrawDialScale(context, centre, plateR, ink, size);

        // Scalloped cap: a soft contact shadow, the cream body, then a glossy domed centre with a
        // top-left highlight. The scallop is static; only the brass pointer rotates with the value.
        StreamGeometry shadowEdge = ScallopedCircle(new Point(centre.X, centre.Y + size * 0.018), capMid, scallopAmp);
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x55, 0x20, 0x18, 0x08)), null, shadowEdge);

        StreamGeometry scallop = ScallopedCircle(centre, capMid, scallopAmp);
        context.DrawGeometry(cap, new Pen(ControlBrush.Halo(ink, 0.30), Math.Max(1, size * 0.01)), scallop);

        double domeR = capMid - scallopAmp - (size * 0.012);
        var dome = new RadialGradientBrush
        {
            Center = new RelativePoint(0.40, 0.34, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.34, 0.26, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.72, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.72, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                new GradientStop(Color.FromArgb(0x30, 0x3A, 0x2C, 0x12), 1),
            },
        };
        context.DrawEllipse(cap, null, centre, domeR, domeR);
        context.DrawEllipse(dome, null, centre, domeR, domeR);

        // Brass pointer: an engraved channel (dark) under a brass fill with a thin highlight, from just
        // off-centre out to the cap edge at the value angle.
        Point inner = PointOnCircle(centre, domeR * 0.08, angle);
        Point tip = PointOnCircle(centre, capMid * 0.96, angle);
        double pw = Math.Max(2.2, size * 0.05);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x3A, 0x2A, 0x0E)), pw + (size * 0.03))
            { LineCap = PenLineCap.Round }, inner, tip);
        context.DrawLine(new Pen(brass, pw) { LineCap = PenLineCap.Round }, inner, tip);
        context.DrawLine(new Pen(ControlBrush.Halo(brass, 0.5), Math.Max(1, pw * 0.32))
            { LineCap = PenLineCap.Round }, inner, tip);
    }

    // Engraved scale on the dial plate: 11 major ticks across the sweep with a minor tick between each.
    private void DrawDialScale(DrawingContext context, Point centre, double plateR, IBrush ink, double size)
    {
        const int majors = 10;
        var majorPen = new Pen(ink, Math.Max(1, size * 0.02)) { LineCap = PenLineCap.Round };
        var minorPen = new Pen(ControlBrush.Halo(ink, 0.55), Math.Max(0.8, size * 0.012)) { LineCap = PenLineCap.Round };

        for (int i = 0; i <= majors * 2; i++)
        {
            double a = StartAngle + (SweepAngle * i / (majors * 2));
            bool major = i % 2 == 0;
            double from = major ? plateR * 0.78 : plateR * 0.82;
            context.DrawLine(major ? majorPen : minorPen,
                PointOnCircle(centre, from, a), PointOnCircle(centre, plateR * 0.90, a));
        }
    }

    // A closed scalloped (piecrust) outline: radius oscillates sinusoidally so the edge reads as soft
    // rounded bumps, like the reference bakelite knob.
    private static StreamGeometry ScallopedCircle(Point centre, double midRadius, double amplitude)
    {
        const int steps = 216;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps * 2 * Math.PI;
                double rr = midRadius + (amplitude * Math.Cos(ScallopBumps * t));
                var p = new Point(centre.X + (rr * Math.Cos(t)), centre.Y + (rr * Math.Sin(t)));
                if (i == 0)
                    ctx.BeginFigure(p, isFilled: true);
                else
                    ctx.LineTo(p);
            }
            ctx.EndFigure(true);
        }
        return geometry;
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
        double step = Detents >= 2 ? 1.0 / (Detents - 1) : KeyStep;
        if (e.Key is Key.Up or Key.Right)
        {
            Value = Math.Clamp(Value + step, 0, 1);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Left)
        {
            Value = Math.Clamp(Value - step, 0, 1);
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
