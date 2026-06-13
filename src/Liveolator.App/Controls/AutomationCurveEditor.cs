using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Liveolator.App.Features.Studio;

namespace Liveolator.App.Controls;

/// <summary>
/// Edits one <see cref="AutomationLaneViewModel"/> curve: draws a piecewise-linear line through its
/// keyframes (held flat before the first / after the last, matching <c>AutomationLane.ValueAt</c>),
/// with a faint 0.5 baseline. Click empty space to add a keyframe, drag a dot to move it (clamped
/// between its neighbours so the curve stays time-ordered), double-click a dot to remove it. Time→x
/// uses the shared <see cref="TimelineMath"/> zoom; value→y uses <see cref="AutomationMath"/> (top = 1).
/// </summary>
public sealed class AutomationCurveEditor : Control
{
    private const double HitTolerancePx = 9;
    private const double DotRadius = 4;

    public static readonly StyledProperty<AutomationLaneViewModel?> LaneProperty =
        AvaloniaProperty.Register<AutomationCurveEditor, AutomationLaneViewModel?>(nameof(Lane));

    public static readonly StyledProperty<double> PixelsPerSecondProperty =
        AvaloniaProperty.Register<AutomationCurveEditor, double>(nameof(PixelsPerSecond), 8.0);

    /// <summary>When true, dragging paints the curve freehand (Ableton-style pencil) instead of editing
    /// individual breakpoints.</summary>
    public static readonly StyledProperty<bool> DrawModeProperty =
        AvaloniaProperty.Register<AutomationCurveEditor, bool>(nameof(DrawMode));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<AutomationCurveEditor, IBrush>(
            nameof(LineBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6)));

    public static readonly StyledProperty<IBrush> BaselineBrushProperty =
        AvaloniaProperty.Register<AutomationCurveEditor, IBrush>(
            nameof(BaselineBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xE8, 0xEE, 0xF6)));

    private AutomationPointViewModel? _drag;
    private bool _painting;

    // Freehand stroke density: paint roughly one keyframe per this many pixels of travel.
    private const double DrawStepPx = 8;

    static AutomationCurveEditor()
    {
        AffectsRender<AutomationCurveEditor>(LaneProperty, PixelsPerSecondProperty, LineBrushProperty, BaselineBrushProperty);
    }

    public AutomationCurveEditor()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public AutomationLaneViewModel? Lane { get => GetValue(LaneProperty); set => SetValue(LaneProperty, value); }
    public double PixelsPerSecond { get => GetValue(PixelsPerSecondProperty); set => SetValue(PixelsPerSecondProperty, value); }
    public bool DrawMode { get => GetValue(DrawModeProperty); set => SetValue(DrawModeProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush BaselineBrush { get => GetValue(BaselineBrushProperty); set => SetValue(BaselineBrushProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LaneProperty)
        {
            Unsubscribe(change.GetOldValue<AutomationLaneViewModel?>());
            Subscribe(change.GetNewValue<AutomationLaneViewModel?>());
            InvalidateVisual();
        }
    }

    private void Subscribe(AutomationLaneViewModel? lane)
    {
        if (lane is null)
            return;
        lane.Points.CollectionChanged += OnPointsChanged;
        foreach (AutomationPointViewModel p in lane.Points)
            p.PropertyChanged += OnPointChanged;
    }

    private void Unsubscribe(AutomationLaneViewModel? lane)
    {
        if (lane is null)
            return;
        lane.Points.CollectionChanged -= OnPointsChanged;
        foreach (AutomationPointViewModel p in lane.Points)
            p.PropertyChanged -= OnPointChanged;
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AutomationPointViewModel p in e.OldItems)
                p.PropertyChanged -= OnPointChanged;
        if (e.NewItems is not null)
            foreach (AutomationPointViewModel p in e.NewItems)
                p.PropertyChanged += OnPointChanged;
        InvalidateVisual();
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        // 0.5 baseline (EQ/filter neutral) so a curve reads against centre.
        var baselinePen = new Pen(BaselineBrush, 1);
        double midY = b.Height * 0.5;
        context.DrawLine(baselinePen, new Point(0, midY), new Point(b.Width, midY));

        if (Lane is not { } lane || lane.Points.Count == 0)
            return;

        var linePen = new Pen(LineBrush, 1.5) { LineJoin = PenLineJoin.Round };
        var dotBrush = LineBrush;

        Point Project(AutomationPointViewModel p) => new(
            TimelineMath.XFromSeconds(p.TimeSeconds, PixelsPerSecond),
            AutomationMath.YFromValue(p.Value, b.Height));

        // Hold flat from the left edge to the first point, then segment-to-segment, then to the right edge.
        Point first = Project(lane.Points[0]);
        context.DrawLine(linePen, new Point(0, first.Y), first);
        for (int i = 1; i < lane.Points.Count; i++)
            context.DrawLine(linePen, Project(lane.Points[i - 1]), Project(lane.Points[i]));
        Point last = Project(lane.Points[^1]);
        context.DrawLine(linePen, last, new Point(b.Width, last.Y));

        foreach (AutomationPointViewModel p in lane.Points)
        {
            Point c = Project(p);
            context.DrawEllipse(dotBrush, null, c, DotRadius, DotRadius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Lane is not { } lane)
            return;

        Point pos = e.GetPosition(this);

        // Freehand draw (Ableton pencil): a drag paints the curve; start the stroke here.
        if (DrawMode)
        {
            _painting = true;
            Paint(lane, pos);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        int hit = NearestIndex(lane, pos);

        if (e.ClickCount >= 2)
        {
            if (hit >= 0)
                lane.RemovePoint(lane.Points[hit]);
            e.Handled = true;
            return;
        }

        _drag = hit >= 0
            ? lane.Points[hit]
            : lane.AddPoint(TimelineMath.SecondsFromX(pos.X, PixelsPerSecond), AutomationMath.ValueFromY(pos.Y, Bounds.Height));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Lane is not { } lane)
            return;

        if (_painting)
        {
            Paint(lane, e.GetPosition(this));
            return;
        }

        if (_drag is null)
            return;

        Point pos = e.GetPosition(this);
        int index = lane.Points.IndexOf(_drag);
        double lower = index > 0 ? lane.Points[index - 1].TimeSeconds : 0;
        double upper = index < lane.Points.Count - 1 ? lane.Points[index + 1].TimeSeconds : double.MaxValue;

        double time = Math.Clamp(TimelineMath.SecondsFromX(pos.X, PixelsPerSecond), lower, upper);
        _drag.TimeSeconds = time;
        _drag.Value = AutomationMath.ValueFromY(pos.Y, Bounds.Height);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        _painting = false;
        e.Pointer.Capture(null);
    }

    // Paint one keyframe at the pointer for a freehand stroke, replacing any within half a step so the
    // stroke leaves ~one point per DrawStepPx of travel.
    private void Paint(AutomationLaneViewModel lane, Point pos)
    {
        double time = TimelineMath.SecondsFromX(pos.X, PixelsPerSecond);
        double value = AutomationMath.ValueFromY(pos.Y, Bounds.Height);
        double tolerance = PixelsPerSecond > 0 ? (DrawStepPx * 0.5) / PixelsPerSecond : 0;
        lane.SetPointAt(time, value, tolerance);
    }

    private int NearestIndex(AutomationLaneViewModel lane, Point pos)
    {
        var pts = new (double Time, double Value)[lane.Points.Count];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = (lane.Points[i].TimeSeconds, lane.Points[i].Value);
        return AutomationMath.NearestPointIndex(pts, pos.X, pos.Y, PixelsPerSecond, Bounds.Height, HitTolerancePx);
    }
}
