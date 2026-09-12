using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Liveolator.App.Controls;

/// <summary>
/// A circular jog wheel (the DJ deck's "platter"): it draws a vinyl-style disc with an accent
/// progress ring + position marker driven by <see cref="Progress"/> (the 0..1 playhead), and turns a
/// click-drag into a seek. Dragging clockwise advances the track, counter-clockwise rewinds it, and the
/// resulting absolute 0..1 fraction is sent to the bound <see cref="SeekCommand"/> — so, like
/// <see cref="Knob"/> and <see cref="Fader"/>, the wheel is pure presentation and every change flows out
/// through the action layer (doc 04). Disabled wheels render neutral and ignore input.
/// </summary>
public partial class Jog : Control
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

    public static readonly StyledProperty<ICommand?> BendCommandProperty =
        AvaloniaProperty.Register<Jog, ICommand?>(nameof(BendCommand));

    public static readonly StyledProperty<ICommand?> BendReleaseCommandProperty =
        AvaloniaProperty.Register<Jog, ICommand?>(nameof(BendReleaseCommand));

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

    /// <summary>Optional artwork drawn at the centre of the platter (clipped to a disc). When set — e.g. a
    /// photoreal jellyfish PNG dropped into the App assets — it replaces the built-in vector medusa; the
    /// bitmap should be roughly square so the disc clip doesn't crop it oddly.</summary>
    public static readonly StyledProperty<IImage?> CenterImageProperty =
        AvaloniaProperty.Register<Jog, IImage?>(nameof(CenterImage));

    /// <summary>The colour the centre medusa is washed toward on the bass (default the reserved red token).
    /// The wash intensity tracks the kick/bass pulse at the playhead (see <see cref="BassTintStrength"/>).</summary>
    public static readonly StyledProperty<IBrush> BassTintBrushProperty =
        AvaloniaProperty.Register<Jog, IBrush>(nameof(BassTintBrush), new SolidColorBrush(Color.FromRgb(0xE5, 0x54, 0x4A)));

    /// <summary>When true (deck playing), the centre medusa turns like a record on a turntable.</summary>
    public static readonly StyledProperty<bool> IsSpinningProperty =
        AvaloniaProperty.Register<Jog, bool>(nameof(IsSpinning));

    /// <summary>Spin tick interval — ~60 fps, smooth enough for the platter without churning the UI thread.</summary>
    private static readonly TimeSpan SpinInterval = TimeSpan.FromMilliseconds(1000.0 / 60.0);

    /// <summary>Clamp the gap between pointer moves before dividing, so a stalled frame (huge dt) or a
    /// burst of moves (tiny dt) can't spike the bend velocity.</summary>
    private const double MinBendDtSeconds = 0.004;
    private const double MaxBendDtSeconds = 0.100;

    private bool _dragging;
    // This drag is a pitch-bend (deck was playing at press), not a scrub-seek. Captured at press so the
    // gesture stays consistent even if playback state flips mid-drag.
    private bool _bendMode;
    private double _lastMoveMs;
    private double _lastAngleRadians;
    private double _baseFraction;
    private double _accumulatedRadians;
    private double _scrubFraction;
    private double _spinRadians;
    private DispatcherTimer? _spinTimer;

    static Jog()
    {
        AffectsRender<Jog>(ProgressProperty, ArcBrushProperty, TrackBrushProperty,
            PlatterBrushProperty, MarkerBrushProperty, IsEnabledProperty,
            KickPeaksProperty, IsKickActiveProperty, GlowBrushProperty,
            CenterImageProperty, BassTintBrushProperty, IsSpinningProperty);
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

    /// <summary>Invoked with the dragged-to absolute 0..1 fraction (the deck's click-to-seek action).
    /// Used while the deck is PAUSED — dragging then scrubs the track to find a cue.</summary>
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    /// <summary>Invoked while the deck is PLAYING with the drag's angular velocity (rev/s, clockwise
    /// positive): the platter then applies a temporary pitch-bend (beat-match nudge) instead of seeking,
    /// like a real DJ jog. Paired with <see cref="BendReleaseCommand"/>.</summary>
    public ICommand? BendCommand { get => GetValue(BendCommandProperty); set => SetValue(BendCommandProperty, value); }

    /// <summary>Invoked (no argument) when a playing-drag is released, so the deck restores its normal rate.</summary>
    public ICommand? BendReleaseCommand { get => GetValue(BendReleaseCommandProperty); set => SetValue(BendReleaseCommandProperty, value); }

    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush PlatterBrush { get => GetValue(PlatterBrushProperty); set => SetValue(PlatterBrushProperty, value); }
    public IBrush MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public IReadOnlyList<float>? KickPeaks { get => GetValue(KickPeaksProperty); set => SetValue(KickPeaksProperty, value); }
    public bool IsKickActive { get => GetValue(IsKickActiveProperty); set => SetValue(IsKickActiveProperty, value); }
    public IBrush GlowBrush { get => GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }
    public IImage? CenterImage { get => GetValue(CenterImageProperty); set => SetValue(CenterImageProperty, value); }
    public IBrush BassTintBrush { get => GetValue(BassTintBrushProperty); set => SetValue(BassTintBrushProperty, value); }
    public bool IsSpinning { get => GetValue(IsSpinningProperty); set => SetValue(IsSpinningProperty, value); }

    private static double CoerceUnit(AvaloniaObject _, double value)
        => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateSpinTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdateSpinTimer();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSpinningProperty || change.Property == IsEnabledProperty)
            UpdateSpinTimer();
    }

    /// <summary>Runs the spin ticker only while the platter is on screen and the deck is playing, so a paused
    /// or off-screen deck costs nothing on the UI thread.</summary>
    private void UpdateSpinTimer()
    {
        bool shouldSpin = _attached && IsSpinning && IsEnabled;
        if (shouldSpin)
        {
            _spinTimer ??= CreateSpinTimer();
            _spinTimer.Start();
        }
        else
        {
            _spinTimer?.Stop();
        }
    }

    private DispatcherTimer CreateSpinTimer()
    {
        var timer = new DispatcherTimer { Interval = SpinInterval };
        timer.Tick += (_, _) =>
        {
            _spinRadians = AdvanceSpin(_spinRadians, SpinInterval.TotalSeconds);
            InvalidateVisual();
        };
        return timer;
    }

    /// <summary>Maps a drag (accumulated signed rotation, radians) onto an absolute 0..1 track fraction,
    /// relative to where the drag started. Clockwise (positive) advances; the result is clamped to the track.</summary>
    internal static double ScrubFraction(double baseFraction, double accumulatedRadians)
    {
        double turns = accumulatedRadians / (2.0 * Math.PI);
        return Math.Clamp(baseFraction + (turns * SeekTrackFractionPerTurn), 0.0, 1.0);
    }

    /// <summary>The drag's angular velocity in signed revolutions/second (clockwise positive), from a
    /// per-move rotation <paramref name="deltaRadians"/> over <paramref name="dtSeconds"/>. The interval is
    /// clamped so a stalled frame or a burst of moves can't spike it; non-finite input yields 0. This is the
    /// input the playing-jog pitch-bend is derived from (via the shared <c>JogMath</c>).</summary>
    internal static double AngularVelocityRevPerSecond(double deltaRadians, double dtSeconds)
    {
        if (!double.IsFinite(deltaRadians) || !double.IsFinite(dtSeconds))
            return 0.0;
        double dt = Math.Clamp(dtSeconds, MinBendDtSeconds, MaxBendDtSeconds);
        return (deltaRadians / (2.0 * Math.PI)) / dt;
    }

    /// <summary>A 12" record spins at 33 1/3 RPM; the centre medusa turns at that real vinyl speed so the
    /// platter reads like a turntable when the deck plays.</summary>
    internal const double RecordRevolutionsPerSecond = 100.0 / 3.0 / 60.0;

    /// <summary>Advances the medusa's spin angle (radians) by <paramref name="deltaSeconds"/> of playback at
    /// record speed, wrapped into one revolution so it never grows without bound. Invalid input holds steady.</summary>
    internal static double AdvanceSpin(double radians, double deltaSeconds)
    {
        if (double.IsNaN(radians))
            radians = 0.0;
        if (double.IsNaN(deltaSeconds) || deltaSeconds <= 0.0)
            deltaSeconds = 0.0;

        double turn = 2.0 * Math.PI;
        double advanced = radians + (deltaSeconds * RecordRevolutionsPerSecond * turn);
        return advanced % turn;
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

    /// <summary>Peak alpha of the medusa's red bass-wash on a full kick. Deliberately low so the wash reads
    /// as a subtle red bloom on the artwork (the owner asked for a gentle tint), never a flat red repaint.</summary>
    internal const double MaxBassTint = 0.32;

    /// <summary>The 0..<see cref="MaxBassTint"/> red-wash strength for a given kick/bass pulse. Linear in the
    /// pulse and capped, so silence is untinted and a hard kick is a gentle red bloom. 0 for invalid input.</summary>
    internal static double BassTintStrength(double pulse)
    {
        if (double.IsNaN(pulse))
            return 0.0;
        return Math.Clamp(pulse, 0.0, 1.0) * MaxBassTint;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        _dragging = true;
        // Playing deck (IsSpinning is bound to IsPlaying) → the drag pitch-bends; paused → it scrubs.
        _bendMode = IsSpinning;
        _lastAngleRadians = AngleAt(e.GetPosition(this));
        if (_bendMode)
        {
            _lastMoveMs = e.Timestamp;
        }
        else
        {
            _baseFraction = Math.Clamp(Progress, 0, 1);
            _scrubFraction = _baseFraction;
            _accumulatedRadians = 0;
        }
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

        if (_bendMode)
        {
            // PLAYING: turn the drag speed into a temporary pitch-bend (nudge), never a seek. Each move
            // re-drives the bend, so the deck holds the nudge while the finger keeps moving and snaps back
            // once it stops (the deck's own restore window) or the drag is released.
            double dtSeconds = (e.Timestamp - _lastMoveMs) / 1000.0;
            _lastMoveMs = e.Timestamp;
            ExecuteBend(AngularVelocityRevPerSecond(delta, dtSeconds));
        }
        else
        {
            // PAUSED: scrub the platter to an absolute position to find a cue.
            _accumulatedRadians += delta;
            double fraction = ScrubFraction(_baseFraction, _accumulatedRadians);
            if (Math.Abs(fraction - _scrubFraction) >= SeekEpsilon)
            {
                _scrubFraction = fraction;
                InvalidateVisual();
                ExecuteSeek(fraction);
            }
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
            // A playing-drag release restores the deck's normal rate immediately (no waiting on a timeout).
            if (_bendMode)
                ExecuteBendRelease();
            _bendMode = false;
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

    private void ExecuteBend(double revPerSecond)
    {
        ICommand? command = BendCommand;
        if (command is not null && command.CanExecute(revPerSecond))
            command.Execute(revPerSecond);
    }

    private void ExecuteBendRelease()
    {
        ICommand? command = BendReleaseCommand;
        if (command is not null && command.CanExecute(null))
            command.Execute(null);
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
