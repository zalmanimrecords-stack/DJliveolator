using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A circular jog wheel (the DJ deck's "platter"): it draws a vinyl-style disc with an accent
/// progress ring + position marker driven by <see cref="Progress"/> (the 0..1 playhead), and turns a
/// click-drag into a seek. Dragging clockwise advances the track, counter-clockwise rewinds it, and the
/// resulting absolute 0..1 fraction is sent to the bound <see cref="SeekCommand"/> — so, like
/// <see cref="Knob"/> and <see cref="Fader"/>, the wheel is pure presentation and every change flows out
/// through the action layer (doc 04). Disabled wheels render neutral and ignore input.
/// </summary>
public class Jog : Control
{
    /// <summary>Track fraction covered by one full 360° drag — coarse enough to scan a track, fine enough
    /// to line a cue up by hand. (A continuously varying step would need the decoded duration, which the
    /// control doesn't have; the playhead readout + waveform give the precise position.)</summary>
    internal const double SeekTrackFractionPerTurn = 0.25;

    /// <summary>Skip dispatching a seek until the scrub position has moved by at least this fraction, so a
    /// single drag doesn't flood the action seam with near-identical absolute seeks.</summary>
    private const double SeekEpsilon = 0.0005;

    /// <summary>12 o'clock — the progress ring fills clockwise from the top, like a transport readout.</summary>
    private const double StartAngle = -90.0;

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<Jog, double>(
            nameof(Progress), defaultValue: 0.0,
            defaultBindingMode: Avalonia.Data.BindingMode.OneWay, coerce: CoerceUnit);

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<Jog, ICommand?>(nameof(SeekCommand));

    public static readonly StyledProperty<IBrush> ArcBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(ArcBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(TrackBrush), new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x40)));

    public static readonly StyledProperty<IBrush> PlatterBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(PlatterBrush), new SolidColorBrush(Color.FromRgb(0x0C, 0x14, 0x22)));

    public static readonly StyledProperty<IBrush> MarkerBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(MarkerBrush), new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)));

    /// <summary>The loaded track's low-frequency (kick/bass) band magnitude per bucket (0..1), aligned 1:1
    /// with the track — the SAME analyzed audio the waveform draws as its kick layer. Sampled at the
    /// playhead so the rim glow flashes on the actual kicks in the sound (not a metronomic grid).</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> KickPeaksProperty =
        AvaloniaProperty.Register<Jog, IReadOnlyList<float>?>(nameof(KickPeaks));

    /// <summary>When true (deck playing), the rim flashes <see cref="GlowBrush"/> on each kick.</summary>
    public static readonly StyledProperty<bool> IsKickActiveProperty =
        AvaloniaProperty.Register<Jog, bool>(nameof(IsKickActive));

    /// <summary>The phosphorescent rim-glow colour pulsed on the kick (default neon green).</summary>
    public static readonly StyledProperty<IBrush> GlowBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(GlowBrush), new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x6A)));

    private bool _dragging;
    private double _lastAngleRadians;
    private double _baseFraction;
    private double _accumulatedRadians;
    private double _scrubFraction;

    static Jog()
    {
        AffectsRender<Jog>(ProgressProperty, ArcBrushProperty, TrackBrushProperty,
            PlatterBrushProperty, MarkerBrushProperty, IsEnabledProperty,
            KickPeaksProperty, IsKickActiveProperty, GlowBrushProperty);
    }

    public Jog()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = 168;
        Height = 168;
    }

    /// <summary>Playhead position as a 0..1 track fraction; drives the progress ring and marker.</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>Invoked with the dragged-to absolute 0..1 fraction (the deck's click-to-seek action).</summary>
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush PlatterBrush { get => GetValue(PlatterBrushProperty); set => SetValue(PlatterBrushProperty, value); }
    public IBrush MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public IReadOnlyList<float>? KickPeaks { get => GetValue(KickPeaksProperty); set => SetValue(KickPeaksProperty, value); }
    public bool IsKickActive { get => GetValue(IsKickActiveProperty); set => SetValue(IsKickActiveProperty, value); }
    public IBrush GlowBrush { get => GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    /// <summary>Maps a drag (accumulated signed rotation, radians) onto an absolute 0..1 track fraction,
    /// relative to where the drag started. Clockwise (positive) advances; the result is clamped to the track.</summary>
    internal static double ScrubFraction(double baseFraction, double accumulatedRadians)
    {
        double turns = accumulatedRadians / (2.0 * Math.PI);
        return Math.Clamp(baseFraction + (turns * SeekTrackFractionPerTurn), 0.0, 1.0);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
            return;

        bool on = IsEnabled;
        double progress = Math.Clamp(_dragging ? _scrubFraction : Progress, 0, 1);
        var centre = new Point(bounds.Width / 2, bounds.Height / 2);
        // Reserve an outer band for the beat (kick) rim glow so it never clips the bounds.
        double glowBand = size * 0.05;
        double outer = (size / 2) - 1 - glowBand;
        double ringStroke = Math.Max(3.0, size * 0.04);
        double ringRadius = outer - (ringStroke / 2);
        double platterRadius = ringRadius - ringStroke - (size * 0.02);
        IBrush arc = on ? ArcBrush : TrackBrush;

        DrawPlatter(context, centre, platterRadius, size);

        // Neutral full-circle track behind the progress arc.
        context.DrawEllipse(null, new Pen(TrackBrush, ringStroke), centre, ringRadius, ringRadius);

        if (on && progress > 0.0008)
        {
            double end = StartAngle + (360.0 * progress);
            context.DrawGeometry(null, new Pen(ControlBrush.Halo(arc, 0.22), ringStroke * 2.2)
                { LineCap = PenLineCap.Round }, Arc(centre, ringRadius, StartAngle, end));
            context.DrawGeometry(null, new Pen(arc, ringStroke)
                { LineCap = PenLineCap.Round }, Arc(centre, ringRadius, StartAngle, end));
        }

        double markerAngle = StartAngle + (360.0 * progress);
        IBrush marker = on ? MarkerBrush : TrackBrush;

        // Spindle line from the hub to the rim marks the play position like a record's start groove.
        context.DrawLine(new Pen(ControlBrush.Halo(marker, on ? 0.85 : 0.5), Math.Max(2.0, size * 0.018))
            { LineCap = PenLineCap.Round },
            PointOnCircle(centre, platterRadius * 0.20, markerAngle),
            PointOnCircle(centre, platterRadius * 0.92, markerAngle));

        if (on)
        {
            Point dot = PointOnCircle(centre, ringRadius, markerAngle);
            context.DrawEllipse(ControlBrush.Halo(arc, 0.35), null, dot, ringStroke * 1.5, ringStroke * 1.5);
            context.DrawEllipse(arc, null, dot, ringStroke * 0.7, ringStroke * 0.7);
        }

        DrawHub(context, centre, platterRadius * 0.20, size);

        // Phosphorescent rim glow that tracks the actual kick in the SOUND: the low-band (kick/bass)
        // magnitude of the analyzed audio, sampled at the playhead. Drawn last so it reads as a frame
        // around the platter; brightest where the track's kick hits, dim between.
        if (on && IsKickActive)
        {
            double pulse = KickEnergyAt(Math.Clamp(Progress, 0, 1), KickPeaks);
            if (pulse > 0.001)
                DrawKickGlow(context, centre, (size / 2) - 1 - (glowBand * 0.5), glowBand, pulse);
        }
    }

    /// <summary>
    /// The 0..1 rim-glow intensity at the playhead, sampled from the track's low-frequency (kick) band
    /// (<see cref="KickPeaks"/>) — so the glow comes from the actual sound, not a metronomic grid. A gamma
    /// emphasises strong transients so the rim flashes on the kick and stays dim otherwise. 0 with no data.
    /// </summary>
    internal static double KickEnergyAt(double progress, IReadOnlyList<float>? kickPeaks)
    {
        if (kickPeaks is null || kickPeaks.Count == 0)
            return 0.0;

        double p = double.IsNaN(progress) ? 0.0 : Math.Clamp(progress, 0.0, 1.0);
        int index = (int)Math.Round(p * (kickPeaks.Count - 1));
        index = Math.Clamp(index, 0, kickPeaks.Count - 1);
        double energy = Math.Clamp(kickPeaks[index], 0.0, 1.0);
        // Gamma > 1 darkens the quiet low-end "floor" and lets the kick transients pop.
        return energy * energy;
    }

    private void DrawKickGlow(DrawingContext context, Point centre, double radius, double band, double pulse)
    {
        // A soft wide halo + a brighter core ring, both in the glow colour and scaled by the pulse.
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(GlowBrush, 0.30 * pulse), band * 1.8), centre, radius, radius);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(GlowBrush, 0.12 + (0.88 * pulse)), band * 0.7), centre, radius, radius);
    }

    private void DrawPlatter(DrawingContext context, Point centre, double radius, double size)
    {
        context.DrawEllipse(PlatterBrush, null, centre, radius, radius);
        // A few faint concentric grooves for the vinyl read; cheap and flat (no blur).
        var groove = new Pen(new SolidColorBrush(Color.FromArgb(0x34, 0x6A, 0x78, 0x8C)), Math.Max(0.6, size * 0.004));
        for (int i = 1; i <= 4; i++)
            context.DrawEllipse(null, groove, centre, radius * (i / 5.0), radius * (i / 5.0));
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x02, 0x06)),
            Math.Max(1, size * 0.012)), centre, radius, radius);
    }

    private void DrawHub(DrawingContext context, Point centre, double radius, double size)
    {
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x16, 0x20, 0x30)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x02, 0x06)), Math.Max(1, size * 0.01)),
            centre, radius, radius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        _dragging = true;
        _baseFraction = Math.Clamp(Progress, 0, 1);
        _scrubFraction = _baseFraction;
        _accumulatedRadians = 0;
        _lastAngleRadians = AngleAt(e.GetPosition(this));
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;

        double angle = AngleAt(e.GetPosition(this));
        double delta = NormalizeAngle(angle - _lastAngleRadians);
        _lastAngleRadians = angle;
        _accumulatedRadians += delta;

        double fraction = ScrubFraction(_baseFraction, _accumulatedRadians);
        if (Math.Abs(fraction - _scrubFraction) >= SeekEpsilon)
        {
            _scrubFraction = fraction;
            InvalidateVisual();
            ExecuteSeek(fraction);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void ExecuteSeek(double fraction)
    {
        ICommand? command = SeekCommand;
        if (command is not null && command.CanExecute(fraction))
            command.Execute(fraction);
    }

    // atan2 in screen space (y grows downward) increases clockwise, so a clockwise drag yields a
    // positive accumulated angle — i.e. forward seek, matching how a record turns.
    private double AngleAt(Point p)
        => Math.Atan2(p.Y - (Bounds.Height / 2), p.X - (Bounds.Width / 2));

    // Fold a raw angle difference into [-π, π] so crossing the ±π seam reads as a small step, not a jump.
    private static double NormalizeAngle(double radians)
    {
        while (radians > Math.PI) radians -= 2.0 * Math.PI;
        while (radians < -Math.PI) radians += 2.0 * Math.PI;
        return radians;
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
