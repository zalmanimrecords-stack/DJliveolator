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
/// The deck waveform strip. When <see cref="Peaks"/> is set it draws the real track overview (mirrored
/// 0..1 magnitudes from <c>WaveformBuilder</c>) with a <see cref="Progress"/> playhead, a played/unplayed
/// split, and an optional <see cref="BeatGrid"/> overlay; with no peaks it falls back to a deterministic
/// decorative graphic (the "no track loaded" placeholder). Clicking the strip computes the clicked 0..1
/// track fraction and invokes <see cref="SeekCommand"/> with it (the deck VM turns that into a DeckSeek
/// action). Purely presentational — the peak/grid data and the seek behaviour come from the view-model.
/// </summary>
public sealed class WaveformStrip : Control
{
    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(BarBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x80, 0xF6)));

    /// <summary>Brush for the part of the waveform already played (left of the playhead).</summary>
    public static readonly StyledProperty<IBrush> PlayedBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(PlayedBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6)));

    /// <summary>Brush for the beat-grid lines (a faint hairline behind the waveform).</summary>
    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(GridBrush), new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xE8, 0xEE, 0xF6)));

    /// <summary>Brush for the low-frequency (kick/bass) band overlay — drawn over the broadband bars so
    /// the kick transients pop, letting a DJ align downbeats by eye for sync.</summary>
    public static readonly StyledProperty<IBrush> KickBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(KickBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xA8, 0x3B)));

    /// <summary>The waveform overview peaks (each 0..1), or null/empty to draw the placeholder.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(Peaks));

    /// <summary>The low-frequency (kick) band peaks (each 0..1), aligned 1:1 with <see cref="Peaks"/>;
    /// null/empty draws no kick overlay.</summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> KickPeaksProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<float>?>(nameof(KickPeaks));

    /// <summary>Beat-line positions as 0..1 track fractions to overlay; null/empty draws no grid.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> BeatGridProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<double>?>(nameof(BeatGrid));

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
            PeaksProperty, KickPeaksProperty, BeatGridProperty, ProgressProperty, ZoomWindowProperty);
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
    public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public IReadOnlyList<float>? KickPeaks { get => GetValue(KickPeaksProperty); set => SetValue(KickPeaksProperty, value); }
    public IReadOnlyList<double>? BeatGrid { get => GetValue(BeatGridProperty); set => SetValue(BeatGridProperty, value); }
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
        RenderWaveform(context, b, peaks, start, span);
        // Kick/bass band drawn ON TOP, glowing, so the kick transients pop — the anchor a DJ aligns for sync.
        IReadOnlyList<float>? kick = KickPeaks;
        if (kick is { Count: > 0 })
            RenderKickBand(context, b, kick, start, span);
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
        var beatPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x55, g.R, g.G, g.B)), 1);
        var barPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0xC8, g.R, g.G, g.B)), 1.4);

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
            context.DrawLine(isBar ? barPen : beatPen, new Point(x, 0), new Point(x, b.Height));
        }
    }

    // Real data: one mirrored bar per column, sampled from the peak buckets within the visible window;
    // bars left of the playhead use the "played" brush.
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
            int index = (int)(TrackFraction(x, b.Width, start, span) * peaks.Count);
            if (index < 0) index = 0;
            else if (index >= peaks.Count) index = peaks.Count - 1;

            double amp = maxAmp * Math.Clamp(peaks[index], 0f, 1f);
            if (amp < 0.5) amp = 0.5; // keep a hairline so silent regions still read as a strip
            Pen pen = x <= playheadX ? playedPen : aheadPen;
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }

        // Playhead: at the window edges (start/end of track) it sits at the strip edge; while zoomed-and-
        // following it rides near the centre. Drawn whenever it is inside the visible window.
        if (playheadX > 0 && playheadX < b.Width)
        {
            var headPen = new Pen(PlayedBrush, 1.5);
            context.DrawLine(headPen, new Point(playheadX, 0), new Point(playheadX, b.Height));
        }
    }

    // The low-frequency (kick) band overlay, drawn over the broadband bars as a GLOWING warm spike on each
    // kick — the beat-align guide for sync. Each column is layered: a wide soft halo, a brighter mid, then
    // a near-white hot core, all derived from KickBrush, so a kick reads as a luminous burst rather than a
    // flat bar. Only the low band above a small floor draws, so quiet sections stay dark and the kicks pop.
    private void RenderKickBand(DrawingContext context, Rect b, IReadOnlyList<float> kick, double start, double span)
    {
        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 2;
        const double step = 2.0;

        Color glow = (KickBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0xFF, 0xA2, 0x2B);
        var haloPen = new Pen(new ImmutableSolidColorBrush(glow, 0.20), 6) { LineCap = PenLineCap.Round };
        var midPen = new Pen(new ImmutableSolidColorBrush(glow, 0.55), 3) { LineCap = PenLineCap.Round };
        var corePen = new Pen(new ImmutableSolidColorBrush(Lighten(glow, 0.45)), 1.6) { LineCap = PenLineCap.Round };

        for (double x = 1; x < b.Width - 1; x += step)
        {
            int index = (int)(TrackFraction(x, b.Width, start, span) * kick.Count);
            if (index < 0) index = 0;
            else if (index >= kick.Count) index = kick.Count - 1;

            double k = Math.Clamp(kick[index], 0f, 1f);
            if (k < 0.06) continue; // suppress the low-band noise floor → only real kicks glow
            // Mild gamma lift so kicks read prominently without washing out the quieter body.
            double amp = maxAmp * Math.Pow(k, 0.7);
            var top = new Point(x, cy - amp);
            var bottom = new Point(x, cy + amp);
            context.DrawLine(haloPen, top, bottom);
            context.DrawLine(midPen, top, bottom);
            context.DrawLine(corePen, top, bottom);
        }
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
