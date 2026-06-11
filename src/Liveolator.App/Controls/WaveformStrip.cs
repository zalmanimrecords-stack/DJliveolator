using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Liveolator.App.Controls;

/// <summary>
/// The deck waveform strip — a layered, kick-forward 3-band view (the best-of blend of the rekordbox
/// 3Band stack and the Mixxx filter split, inverted so the KICK is the front layer): pale high-band
/// caps in back, the mid band as the body, and the low/kick band drawn LAST in a bright glow — the
/// transient anchor a DJ beat-aligns by eye. With only broadband <see cref="Peaks"/> (no band data) it
/// falls back to the single-body render; with no peaks it draws the "no track loaded" placeholder.
/// Also overlays a <see cref="Progress"/> playhead, a played/unplayed split, and an optional
/// <see cref="BeatGrid"/>. Clicking the strip computes the clicked 0..1 track fraction and invokes
/// <see cref="SeekCommand"/> with it (the deck VM turns that into a DeckSeek action). Purely
/// presentational — the peak/grid data and the seek behaviour come from the view-model.
/// </summary>
public sealed class WaveformStrip : Control
{
    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(BarBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x80, 0xF6)));

    /// <summary>Brush for the part of the waveform already played (left of the playhead); also the
    /// playhead and bar-line accent.</summary>
    public static readonly StyledProperty<IBrush> PlayedBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(PlayedBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6)));

    /// <summary>Brush for the beat-grid lines (a faint hairline behind the waveform).</summary>
    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(GridBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xE8, 0xEE, 0xF6)));

    /// <summary>Brush for the low-frequency (kick/bass) band — the FRONT layer, drawn last as a bright
    /// amber halo+core glow over the body so the kick transients pop, letting a DJ align downbeats by
    /// eye for sync.</summary>
    public static readonly StyledProperty<IBrush> KickBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(KickBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xA8, 0x3B)));

    /// <summary>Brush for the mid band — the waveform body, behind the kick layer.</summary>
    public static readonly StyledProperty<IBrush> MidBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(MidBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x3D, 0x5C, 0x8F)));

    /// <summary>Brush for the high band — thin pale caps in the back layer ("air"/hat texture). The
    /// translucency rides in the colour's alpha so theme overrides keep it.</summary>
    public static readonly StyledProperty<IBrush> HighBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(HighBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x8C, 0xDC, 0xE6, 0xF4)));

    /// <summary>The waveform overview peaks (each 0..1), or null/empty to draw the placeholder.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(Peaks));

    /// <summary>The low-frequency (kick) band peaks (each 0..1), aligned 1:1 with <see cref="Peaks"/>;
    /// null/empty draws no kick overlay.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> KickPeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(KickPeaks));

    /// <summary>The mid band peaks (each 0..1), aligned 1:1 with <see cref="Peaks"/>. With
    /// <see cref="HighPeaks"/> present they switch the strip to the layered 3-band render.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> MidPeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(MidPeaks));

    /// <summary>The high band peaks (each 0..1), aligned 1:1 with <see cref="Peaks"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> HighPeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(HighPeaks));

    /// <summary>Beat-line positions as 0..1 track fractions to overlay; null/empty draws no grid.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> BeatGridProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<double>?>(nameof(BeatGrid));

    public static readonly StyledProperty<double?> KickAnchorProperty =
        AvaloniaProperty.Register<WaveformStrip, double?>(nameof(KickAnchor));

    /// <summary>Playhead position as a 0..1 fraction of the track.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformStrip, double>(nameof(Progress));

    /// <summary>
    /// Zoom window as a fraction of the track to show centred on the playhead: <c>0</c> (or ≥1) draws the
    /// whole-track overview; a small value (e.g. 0.03) magnifies a window around <see cref="Progress"/> and
    /// — because <see cref="Progress"/> advances during playback — the wave scrolls (follow), so the two
    /// decks' kicks can be lined up by eye for sync.
    /// </summary>
    public static readonly StyledProperty<double> ZoomWindowProperty =
        AvaloniaProperty.Register<WaveformStrip, double>(nameof(ZoomWindow));

    /// <summary>Invoked on click with the clicked 0..1 track fraction (click-to-seek).</summary>
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformStrip, ICommand?>(nameof(SeekCommand));

    static WaveformStrip()
    {
        AffectsRender<WaveformStrip>(
            BarBrushProperty, PlayedBrushProperty, GridBrushProperty, KickBrushProperty,
            MidBrushProperty, HighBrushProperty,
            PeaksProperty, KickPeaksProperty, MidPeaksProperty, HighPeaksProperty,
            BeatGridProperty, KickAnchorProperty,
            ProgressProperty, ZoomWindowProperty);
    }

    public WaveformStrip()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush PlayedBrush { get => GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush KickBrush { get => GetValue(KickBrushProperty); set => SetValue(KickBrushProperty, value); }
    public IBrush MidBrush { get => GetValue(MidBrushProperty); set => SetValue(MidBrushProperty, value); }
    public IBrush HighBrush { get => GetValue(HighBrushProperty); set => SetValue(HighBrushProperty, value); }
    public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public IReadOnlyList<float>? KickPeaks { get => GetValue(KickPeaksProperty); set => SetValue(KickPeaksProperty, value); }
    public IReadOnlyList<float>? MidPeaks { get => GetValue(MidPeaksProperty); set => SetValue(MidPeaksProperty, value); }
    public IReadOnlyList<float>? HighPeaks { get => GetValue(HighPeaksProperty); set => SetValue(HighPeaksProperty, value); }
    public IReadOnlyList<double>? BeatGrid { get => GetValue(BeatGridProperty); set => SetValue(BeatGridProperty, value); }
    public double? KickAnchor { get => GetValue(KickAnchorProperty); set => SetValue(KickAnchorProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double ZoomWindow { get => GetValue(ZoomWindowProperty); set => SetValue(ZoomWindowProperty, value); }
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    /// <summary>
    /// Maps a clicked X coordinate to a 0..1 track fraction. Pure so it unit-tests without a render: a
    /// non-positive width yields 0; the result is clamped to 0..1.
    /// </summary>
    public static double FractionFromX(double x, double width)
    {
        if (width <= 0 || double.IsNaN(x) || double.IsNaN(width))
            return 0;
        return Math.Clamp(x / width, 0.0, 1.0);
    }

    /// <summary>
    /// The visible 0..1 track window for a given playhead <paramref name="progress"/> and
    /// <paramref name="zoomWindow"/> (fraction of the track to show). Returns the whole track
    /// <c>(0,1)</c> when the zoom is ≤0 or ≥1; otherwise a window of width <paramref name="zoomWindow"/>
    /// centred on the playhead, clamped to stay inside the track at the ends. Pure, so the
    /// mapping unit-tests without a render.
    /// </summary>
    public static (double Start, double Span) VisibleWindow(double progress, double zoomWindow)
    {
        if (double.IsNaN(zoomWindow) || zoomWindow <= 0 || zoomWindow >= 1)
            return (0.0, 1.0);
        double p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0.0, 1.0);
        double start = p - (zoomWindow / 2.0);
        if (start < 0) start = 0;
        else if (start + zoomWindow > 1) start = 1 - zoomWindow;
        return (start, zoomWindow);
    }

    public static double? MarkerX(double fraction, double start, double span, double width)
    {
        if (span <= 0 || width <= 0 || fraction < start || fraction > start + span)
            return null;
        return (fraction - start) / span * width;
    }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        IReadOnlyList<float>? peaks = Peaks;
        if (peaks is not { Count: > 0 })
        {
            RenderEmpty(context, b); // no track → an explicit empty state, never a fake waveform
            return;
        }

        // The visible window: whole track when not zoomed, or a magnified slice centred on the playhead
        // that scrolls as Progress advances (follow). All overlays use the same window so they stay aligned.
        (double start, double span) = VisibleWindow(Progress, ZoomWindow);

        RenderBeatGrid(context, b, start, span);
        RenderKickAnchor(context, b, start, span);

        // Layer order back→front (the kick-forward stack): pale highs give air/hat texture, the mid
        // band is the body, and the kick band draws LAST so its transients sit in front of everything.
        // Without band data (older/fake overviews) the broadband body renders instead — never nothing.
        IReadOnlyList<float>? mid = MidPeaks;
        IReadOnlyList<float>? high = HighPeaks;
        if (mid is { Count: > 0 } && high is { Count: > 0 })
        {
            RenderBand(context, b, high, HighBrush, start, span);
            RenderBand(context, b, mid, MidBrush, start, span);
        }
        else
        {
            RenderWaveform(context, b, peaks, start, span);
        }

        IReadOnlyList<float>? kick = KickPeaks;
        if (kick is { Count: > 0 })
            RenderKickBand(context, b, kick, start, span);

        // Playhead above every layer, so the current position is never buried under a kick bar.
        RenderPlayhead(context, b, start, span);
    }

    private void RenderKickAnchor(DrawingContext context, Rect b, double start, double span)
    {
        if (KickAnchor is not { } anchor || MarkerX(anchor, start, span, b.Width) is not { } x)
            return;

        Color kick = (KickBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0xF2, 0xA8, 0x3B);
        var halo = new Pen(new ImmutableSolidColorBrush(kick, 0.35), 5);
        var core = new Pen(new ImmutableSolidColorBrush(Lighten(kick, 0.35)), 2);
        context.DrawLine(halo, new Point(x, 0), new Point(x, b.Height));
        context.DrawLine(core, new Point(x, 0), new Point(x, b.Height));
    }

    // Maps a visible-window column x to its 0..1 track fraction.
    private static double TrackFraction(double x, double width, double start, double span)
        => start + (width <= 0 ? 0 : x / width) * span;

    /// <summary>Beats per bar (4/4): every fourth grid line (index 0, 4, 8 …) is a bar downbeat, drawn
    /// brighter so the grid reads as bars. The grid list is anchored on the first beat, so index 0 is a bar.</summary>
    private const int BeatsPerBar = 4;

    // Beat/bar grid within the visible window: bar lines (every 4th, from the first-beat anchor) are drawn
    // bright; beat lines faint. Adaptive — lines that would be too dense to read are skipped, so the grid
    // is hidden in the whole-track overview and resolves into bars, then beats, as the strip zooms in.
    private void RenderBeatGrid(DrawingContext context, Rect b, double start, double span)
    {
        IReadOnlyList<double>? grid = BeatGrid;
        if (grid is not { Count: >= 2 } || span <= 0)
            return;

        double stepFraction = grid[1] - grid[0]; // even spacing → one beat
        if (stepFraction <= 0)
            return;
        double beatPx = stepFraction / span * b.Width;
        bool drawBeats = beatPx >= 7.0;
        bool drawBars = beatPx * BeatsPerBar >= 7.0;
        if (!drawBars)
            return; // too zoomed-out to read even bar lines → draw no grid (keeps the overview clean)

        Color g = (GridBrush as ISolidColorBrush)?.Color ?? Color.FromArgb(0x40, 0xE8, 0xEE, 0xF6);
        // Downbeat (bar) lines are the alignment markers: a crisp, opaque BLUE line (the single accent),
        // with a faint blue halo so it stays visible behind the amber kick. The amber kick column sitting
        // centred on a blue downbeat line is the "kick is on the grid" read used to stack A over B. Beat
        // (non-downbeat) lines stay a faint hairline so the bars dominate.
        Color accent = (PlayedBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0x2F, 0x80, 0xF6);
        var beatPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x55, g.R, g.G, g.B)), 1);
        var barHaloPen = new Pen(new ImmutableSolidColorBrush(accent, 0.28), 3);
        var barPen = new Pen(new ImmutableSolidColorBrush(accent), 1.4);

        double end = start + span;
        for (int i = 0; i < grid.Count; i++)
        {
            double fraction = grid[i];
            if (fraction < start || fraction > end)
                continue;
            bool isBar = i % BeatsPerBar == 0;
            if (!isBar && !drawBeats)
                continue;
            double x = (fraction - start) / span * b.Width;
            if (isBar)
            {
                context.DrawLine(barHaloPen, new Point(x, 0), new Point(x, b.Height));
                context.DrawLine(barPen, new Point(x, 0), new Point(x, b.Height));
            }
            else
            {
                context.DrawLine(beatPen, new Point(x, 0), new Point(x, b.Height));
            }
        }
    }

    /// <summary>
    /// Peak-HOLD over every bucket that falls in the pixel column [x, x+step): the maximum band value
    /// in that range, never a single sampled index — so a kick can't fall between columns (no missed
    /// kicks, no flicker while scrolling). Pure and public so the sampling unit-tests without a render.
    /// </summary>
    public static float ColumnPeak(
        IReadOnlyList<float> values, double x, double step, double width, double start, double span)
    {
        int count = values.Count;
        if (count == 0)
            return 0f;
        int i0 = (int)(TrackFraction(x, width, start, span) * count);
        int i1 = (int)(TrackFraction(x + step, width, start, span) * count);
        if (i0 > i1) (i0, i1) = (i1, i0);
        if (i0 < 0) i0 = 0;
        if (i1 >= count) i1 = count - 1;

        float peak = 0f;
        for (int i = i0; i <= i1; i++)
            if (values[i] > peak) peak = values[i];
        return peak;
    }

    // Broadband fallback (no band data): one mirrored bar per column, peak-held within the visible
    // window; bars left of the playhead use the "played" brush.
    private void RenderWaveform(DrawingContext context, Rect b, IReadOnlyList<float> peaks, double start, double span)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 2;
        const double step = 2.0;
        double playheadX = (Math.Clamp(Progress, 0.0, 1.0) - start) / span * b.Width;

        var playedPen = new Pen(PlayedBrush, 1.5) { LineCap = PenLineCap.Round };
        var aheadPen = new Pen(BarBrush, 1.5) { LineCap = PenLineCap.Round };

        for (double x = 1; x < b.Width - 1; x += step)
        {
            double amp = maxAmp * Math.Clamp(ColumnPeak(peaks, x, step, b.Width, start, span), 0f, 1f);
            if (amp < 0.5) amp = 0.5; // keep a hairline so silent regions still read as a strip
            Pen pen = x <= playheadX ? playedPen : aheadPen;
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }
    }

    // One mirrored band layer (high caps / mid body): a bar per pixel column, peak-held. The part still
    // ahead of the playhead is dimmed (same played/ahead split the broadband body uses), per band so the
    // stack keeps its depth on both sides of the playhead.
    private void RenderBand(
        DrawingContext context, Rect b, IReadOnlyList<float> band, IBrush brush, double start, double span)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 2;
        const double step = 1.0;
        double playheadX = (Math.Clamp(Progress, 0.0, 1.0) - start) / span * b.Width;

        Color color = (brush as ISolidColorBrush)?.Color ?? Color.FromRgb(0x3D, 0x5C, 0x8F);
        double opacity = (brush as ISolidColorBrush)?.Opacity ?? 1.0;
        var playedPen = new Pen(new ImmutableSolidColorBrush(color, opacity), 1.0);
        var aheadPen = new Pen(new ImmutableSolidColorBrush(color, opacity * 0.55), 1.0);

        for (double x = 1; x < b.Width - 1; x += step)
        {
            float v = ColumnPeak(band, x, step, b.Width, start, span);
            if (v <= 0.004f) // skip true silence — the broadband hairline already keeps the strip readable
                continue;
            double amp = maxAmp * Math.Clamp(v, 0f, 1f);
            Pen pen = x <= playheadX ? playedPen : aheadPen;
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }
    }

    /// <summary>Kick columns at or above this level get a white-hot core — the hardest transients
    /// read hotter than the rest, like a meter clipping into white.</summary>
    public const float KickHotThreshold = 0.85f;

    // The low-frequency (kick) band — the FRONT layer, drawn last so kicks sit in front of the body.
    // Two passes per column fake a cheap bloom with zero per-frame effect objects: a wide translucent
    // halo underneath, then a crisp 1 px opaque core. Hot kicks (≥ KickHotThreshold) lighten the core
    // toward white in quantized steps (pens are pre-built — no per-column allocation). One bar per
    // pixel column with peak-HOLD, so a kick never falls between samples and reads as a hard marker.
    // Only the low band above a floor draws, so quiet sections stay dark and the kicks stand out —
    // line them up on the blue downbeat to stack A over B.
    private void RenderKickBand(DrawingContext context, Rect b, IReadOnlyList<float> kick, double start, double span)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 2;
        const double step = 1.0;     // one bar per pixel column — no horizontal blur
        const float floor = 0.08f;   // suppress the low-band noise floor → only real kicks light up

        Color amber = (KickBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0xF2, 0xA8, 0x3B);
        var haloPen = new Pen(new ImmutableSolidColorBrush(amber, 0.30), 3.0) { LineCap = PenLineCap.Round };
        // Quantized hot-core pens: index 0 = the plain band colour, 3 = hottest (whitest) core.
        var corePens = new Pen[4];
        for (int level = 0; level < corePens.Length; level++)
            corePens[level] = new Pen(
                new ImmutableSolidColorBrush(Lighten(amber, level * 0.25)), 1.0) { LineCap = PenLineCap.Round };

        for (double x = 1; x < b.Width - 1; x += step)
        {
            float k = ColumnPeak(kick, x, step, b.Width, start, span);
            if (k < floor) continue;

            // Mild gamma so kicks read prominently; halo under a solid core gives glow + a hard edge.
            double amp = maxAmp * Math.Pow(Math.Clamp(k, 0f, 1f), 0.8);
            var top = new Point(x, cy - amp);
            var bottom = new Point(x, cy + amp);
            context.DrawLine(haloPen, top, bottom);
            context.DrawLine(corePens[HotLevel(k)], top, bottom);
        }
    }

    /// <summary>Maps a kick level to its quantized hot-core step (0 = normal colour, 3 = white-hot).
    /// Pure and public so the hot-kick contract unit-tests without a render.</summary>
    public static int HotLevel(float k)
        => k >= 0.97f ? 3 : k >= 0.91f ? 2 : k >= KickHotThreshold ? 1 : 0;

    // Playhead: at the window edges (start/end of track) it sits at the strip edge; while zoomed-and-
    // following it rides near the centre. Drawn whenever it is inside the visible window, above all
    // waveform layers.
    private void RenderPlayhead(DrawingContext context, Rect b, double start, double span)
    {
        double playheadX = (Math.Clamp(Progress, 0.0, 1.0) - start) / span * b.Width;
        if (playheadX <= 0 || playheadX >= b.Width)
            return;
        var headPen = new Pen(PlayedBrush, 1.5);
        context.DrawLine(headPen, new Point(playheadX, 0), new Point(playheadX, b.Height));
    }

    // Blend a colour toward white by t (0..1) for the hot glow core.
    private static Color Lighten(Color c, double t)
    {
        static byte Up(byte v, double t) => (byte)Math.Clamp(v + (255 - v) * t, 0, 255);
        return Color.FromArgb(c.A, Up(c.R, t), Up(c.G, t), Up(c.B, t));
    }

    // No track loaded: a calm baseline + a centred label, so the empty deck reads as "no track" rather
    // than a misleading decorative waveform.
    private void RenderEmpty(DrawingContext context, Rect b)
    {
        double cy = b.Height / 2;
        var line = new Pen(GridBrush, 1);
        context.DrawLine(line, new Point(6, cy), new Point(b.Width - 6, cy));

        var label = new FormattedText(
            "NO TRACK", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, new ImmutableSolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)));
        context.DrawText(label, new Point((b.Width - label.Width) / 2, cy - (label.Height / 2)));
    }

    // Click-to-seek: only when a track is loaded (peaks present) and a command is bound, so clicking the
    // empty strip does nothing. The clicked column maps through the visible window to a track fraction, so
    // seeking is correct whether the strip is showing the whole track or a zoomed-in slice.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ICommand? command = SeekCommand;
        if (command is null || Peaks is not { Count: > 0 })
            return;

        double viewFraction = FractionFromX(e.GetPosition(this).X, Bounds.Width);
        (double start, double span) = VisibleWindow(Progress, ZoomWindow);
        double trackFraction = Math.Clamp(start + (viewFraction * span), 0.0, 1.0);
        if (command.CanExecute(trackFraction))
            command.Execute(trackFraction);
        Focus();
        e.Handled = true;
    }
}
