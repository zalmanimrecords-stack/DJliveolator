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
/// The deck waveform strip — a layered, kick-forward 3-band waveform in the top
/// region (blue/cyan high-band caps in back, the green mid band as the body, and the red low/kick band
/// drawn LAST in a bright glow — the transient anchor a DJ beat-aligns by eye), over a dedicated CBG
/// "comb" strip pinned to the bottom that carries the beat marking (short grey beat teeth + broad red
/// downbeat blocks every fourth beat). With only broadband <see cref="Peaks"/> (no band data) the wave
/// falls back to the single-body render; with no peaks it draws the "no track loaded" placeholder.
/// Also overlays a near-white <see cref="Progress"/> playhead and (in the comb) an optional
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
    /// RED halo+core glow over the body (the bass-is-red scheme), so the kick transients pop and
    /// a DJ can align downbeats by eye for sync.</summary>
    public static readonly StyledProperty<IBrush> KickBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(KickBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xE2, 0x3B, 0x2E)));

    /// <summary>Brush for the mid band — the waveform body (green), behind the kick layer.</summary>
    public static readonly StyledProperty<IBrush> MidBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(MidBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x39, 0xC2, 0x4A)));

    /// <summary>Brush for the high band — thin blue/cyan caps in the back layer ("air"/hat texture, the
    /// treble colour). The translucency rides in the colour's alpha so theme overrides keep it.</summary>
    public static readonly StyledProperty<IBrush> HighBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(HighBrush), new ImmutableSolidColorBrush(Color.FromArgb(0xA0, 0x36, 0xA6, 0xE8)));

    /// <summary>Brush for the playhead — a crisp near-white vertical line, drawn over
    /// the wave and the beat comb.</summary>
    public static readonly StyledProperty<IBrush> PlayheadBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(PlayheadBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)));

    /// <summary>Brush for the regular (non-downbeat) beat blocks in the bottom CBG comb — a desaturated
    /// grey tooth, kept faint so the red downbeats dominate.</summary>
    public static readonly StyledProperty<IBrush> BeatBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(BeatBrush), new ImmutableSolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8)));

    /// <summary>Brush for the downbeat (bar-start) blocks in the bottom CBG comb — a broad red tooth
    /// every fourth beat (the "beginning of a measure" marker).</summary>
    public static readonly StyledProperty<IBrush> DownbeatBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(DownbeatBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x40, 0x3A)));

    /// <summary>When <c>true</c>, the CBG beat comb is drawn at the TOP of the strip instead of the bottom.
    /// The lower deck in a stacked pair sets this so the two decks' beat markers sit ADJACENT — meeting in
    /// the middle between the strips (the combined-waveform read), not split to the outer edges.</summary>
    /// <summary>Brush for the hot-cue markers — a vertical line per stored cue (the library overview uses
    /// these to show WHERE the track's hot cues sit). A distinct warm colour so cues read over the wave.</summary>
    public static readonly StyledProperty<IBrush> CueBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(CueBrush), new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xC8, 0x3B)));

    public static readonly StyledProperty<bool> CombAtTopProperty =
        AvaloniaProperty.Register<WaveformStrip, bool>(nameof(CombAtTop));

    /// <summary>When <c>true</c>, the waveform is drawn FOLDED (single-sided) to a baseline at the comb edge
    /// and grows AWAY from the comb, instead of the default centred (symmetric) bars. A stacked pair with
    /// the lower deck's comb on top then forms the combined "butterfly": the upper deck grows up,
    /// the lower deck grows down, mirroring around the shared central comb.</summary>
    public static readonly StyledProperty<bool> FoldedProperty =
        AvaloniaProperty.Register<WaveformStrip, bool>(nameof(Folded));

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

    /// <summary>Which <see cref="BeatGrid"/> line starts the bar (0..3 for 4/4): comb line <c>i</c> is drawn
    /// as a red bar downbeat when <c>((i - DownbeatOffset) mod 4) == 0</c>. 0 puts the downbeat on index 0
    /// (the grid's first beat); the deck sets it from the analyzed/edited downbeat so the bars sit on the
    /// musical "one" rather than on an arbitrary beat.</summary>
    public static readonly StyledProperty<int> DownbeatOffsetProperty =
        AvaloniaProperty.Register<WaveformStrip, int>(nameof(DownbeatOffset));

    public static readonly StyledProperty<double?> KickAnchorProperty =
        AvaloniaProperty.Register<WaveformStrip, double?>(nameof(KickAnchor));

    /// <summary>Hot-cue positions as 0..1 track fractions to overlay as vertical markers; null/empty draws
    /// none. Presentational only — the library overview passes the selected track's stored cues here.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> HotCueMarkersProperty =
        AvaloniaProperty.Register<WaveformStrip, IReadOnlyList<double>?>(nameof(HotCueMarkers));

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
            PlayheadBrushProperty, BeatBrushProperty, DownbeatBrushProperty, CombAtTopProperty, FoldedProperty,
            CueBrushProperty,
            PeaksProperty, KickPeaksProperty, MidPeaksProperty, HighPeaksProperty,
            BeatGridProperty, DownbeatOffsetProperty, KickAnchorProperty, HotCueMarkersProperty,
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
    public IBrush PlayheadBrush { get => GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }
    public IBrush BeatBrush { get => GetValue(BeatBrushProperty); set => SetValue(BeatBrushProperty, value); }
    public IBrush DownbeatBrush { get => GetValue(DownbeatBrushProperty); set => SetValue(DownbeatBrushProperty, value); }
    public IBrush CueBrush { get => GetValue(CueBrushProperty); set => SetValue(CueBrushProperty, value); }
    public bool CombAtTop { get => GetValue(CombAtTopProperty); set => SetValue(CombAtTopProperty, value); }
    public bool Folded { get => GetValue(FoldedProperty); set => SetValue(FoldedProperty, value); }
    public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public IReadOnlyList<float>? KickPeaks { get => GetValue(KickPeaksProperty); set => SetValue(KickPeaksProperty, value); }
    public IReadOnlyList<float>? MidPeaks { get => GetValue(MidPeaksProperty); set => SetValue(MidPeaksProperty, value); }
    public IReadOnlyList<float>? HighPeaks { get => GetValue(HighPeaksProperty); set => SetValue(HighPeaksProperty, value); }
    public IReadOnlyList<double>? BeatGrid { get => GetValue(BeatGridProperty); set => SetValue(BeatGridProperty, value); }
    public int DownbeatOffset { get => GetValue(DownbeatOffsetProperty); set => SetValue(DownbeatOffsetProperty, value); }
    public double? KickAnchor { get => GetValue(KickAnchorProperty); set => SetValue(KickAnchorProperty, value); }
    public IReadOnlyList<double>? HotCueMarkers { get => GetValue(HotCueMarkersProperty); set => SetValue(HotCueMarkersProperty, value); }
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
    /// The visible track window for a given playhead <paramref name="progress"/> and
    /// <paramref name="zoomWindow"/> (fraction of the track to show). Returns the whole track
    /// <c>(0,1)</c> when the zoom is ≤0 or ≥1; otherwise a window of width <paramref name="zoomWindow"/>
    /// centred on the playhead. Near the head/tail the window is NOT clamped to the track — it extends
    /// past it (drawn empty via <see cref="ColumnInTrack"/>) so the playhead stays dead-centre right to
    /// the ends, which keeps two synced decks beat-locked through an intro/outro blend. Pure, so the
    /// mapping unit-tests without a render.
    /// </summary>
    public static (double Start, double Span) VisibleWindow(double progress, double zoomWindow)
    {
        if (double.IsNaN(zoomWindow) || zoomWindow <= 0 || zoomWindow >= 1)
            return (0.0, 1.0);
        double p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0.0, 1.0);
        return (p - (zoomWindow / 2.0), zoomWindow);
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

        // Combined-waveform layout: the waveform body sits clean, and the beat marking lives in a dedicated CBG comb
        // strip. The comb is normally pinned to the BOTTOM; the lower deck flips it to the TOP (CombAtTop) so
        // the two stacked decks' beat markers sit ADJACENT, meeting in the middle (the combined-view read).
        double combH = CombHeight(b.Height);
        bool combAtTop = CombAtTop;
        double combY = combAtTop ? 0 : b.Height - combH;
        double waveTop = combAtTop ? combH : 0;
        Rect waveRect = b.WithHeight(b.Height - combH);

        // Shift the wave layers to the side of the comb so they never overlap it (the comb owns its band,
        // the wave owns the rest). A pure Y translation keeps the per-layer draw maths unchanged.
        // Centred bars by default; folded (single-sided, growing away from the comb) for the combined
        // butterfly view so a stacked A/B pair mirrors around the shared central comb.
        WaveGeometry geometry = WaveGeometry.For(waveRect.Height, Folded, combAtTop);

        using (context.PushTransform(Matrix.CreateTranslation(0, waveTop)))
        {
            RenderKickAnchor(context, waveRect, start, span);

            // Layer order back→front (the kick-forward stack): blue/cyan highs give air/hat texture, the green
            // mid band is the body, and the red kick band draws LAST so its transients sit in front of everything.
            // Without band data (older/fake overviews) the broadband body renders instead — never nothing.
            IReadOnlyList<float>? mid = MidPeaks;
            IReadOnlyList<float>? high = HighPeaks;
            if (mid is { Count: > 0 } && high is { Count: > 0 })
            {
                RenderBand(context, waveRect, high, HighBrush, geometry, start, span);
                RenderBand(context, waveRect, mid, MidBrush, geometry, start, span);
            }
            else
            {
                RenderWaveform(context, waveRect, peaks, geometry, start, span);
            }

            IReadOnlyList<float>? kick = KickPeaks;
            if (kick is { Count: > 0 })
                RenderKickBand(context, waveRect, kick, geometry, start, span);
        }

        // The CBG comb (beat marking), then the playhead over everything so the current position is never
        // buried under a kick bar or a comb tooth.
        RenderBeatComb(context, b, combY, combH, combAtTop, start, span);
        RenderCueMarkers(context, b, start, span);
        RenderPlayhead(context, b, start, span);
    }

    // Hot-cue markers: a full-height vertical line per stored cue, over the wave (the library overview shows
    // WHERE the track's cues sit). Uses the same MarkerX window mapping as the kick anchor, so a cue outside
    // the visible window is skipped rather than clamped to an edge.
    private void RenderCueMarkers(DrawingContext context, Rect b, double start, double span)
    {
        IReadOnlyList<double>? markers = HotCueMarkers;
        if (markers is not { Count: > 0 })
            return;

        var pen = new Pen(CueBrush, 1.5);
        foreach (double fraction in markers)
            if (MarkerX(fraction, start, span, b.Width) is { } x)
                context.DrawLine(pen, new Point(x, 0), new Point(x, b.Height));
    }

    private const double CombHeightFraction = 0.16;
    private const double CombMinHeight = 9.0;
    private const double CombMaxHeight = 15.0;

    /// <summary>
    /// Height of the bottom CBG comb strip for a strip of total <paramref name="totalHeight"/>: a fixed
    /// fraction clamped to a readable band (so the comb stays legible on a tall LIVE strip and never eats
    /// the whole wave on a short one). Pure and public so the layout split unit-tests without a render.
    /// </summary>
    public static double CombHeight(double totalHeight)
    {
        if (double.IsNaN(totalHeight) || totalHeight <= 0)
            return 0;
        double h = Math.Clamp(totalHeight * CombHeightFraction, CombMinHeight, CombMaxHeight);
        double half = totalHeight * 0.5; // never take more than half the strip on a very short control
        if (h > half) h = half;
        // Whole pixels: the lower deck offsets its wave DOWN by this height (a transform), so a fractional
        // value would land the wave on a sub-pixel boundary and blur it. Rounding keeps both decks crisp.
        return Math.Round(h);
    }

    // The detected first-beat (downbeat anchor): a faint, neutral full-height hint in the wave area used to
    // line decks A/B up by eye. Kept subtle because the red downbeat teeth in the comb already mark the bars.
    private void RenderKickAnchor(DrawingContext context, Rect b, double start, double span)
    {
        if (KickAnchor is not { } anchor || MarkerX(anchor, start, span, b.Width) is not { } x)
            return;

        Color head = (PlayheadBrush as ISolidColorBrush)?.Color ?? Colors.White;
        var pen = new Pen(new ImmutableSolidColorBrush(head, 0.22), 1);
        context.DrawLine(pen, new Point(x, 0), new Point(x, b.Height));
    }

    // Maps a visible-window column x to its 0..1 track fraction.
    private static double TrackFraction(double x, double width, double start, double span)
        => start + (width <= 0 ? 0 : x / width) * span;

    // True when the pixel column at x maps to a position inside the track [0,1). When the centred window
    // extends past the head/tail (VisibleWindow no longer clamps), the out-of-track columns are skipped so
    // the strip draws EMPTY there instead of smearing the first/last sample across the lead-in/out.
    private static bool ColumnInTrack(double x, double width, double start, double span)
    {
        double f = TrackFraction(x, width, start, span);
        return f >= 0 && f < 1;
    }

    /// <summary>Beats per bar (4/4): every fourth comb tooth is a bar downbeat, drawn as a broad red block.
    /// Which line is the downbeat is set by <see cref="DownbeatOffset"/> (the analyzed/edited "one"), not a
    /// fixed index 0 — so the red bars sit on the musical one rather than on whatever beat the grid starts on.</summary>
    private const int BeatsPerBar = 4;

    // The CBG comb (beat marking): a row of bottom-anchored teeth in the comb strip.
    // Regular beats are short faint grey blocks; every 4th (a bar downbeat) is a broad red block running
    // the full comb height, with a soft halo. Adaptive — teeth too dense to read are skipped, so the comb
    // is empty in the whole-track overview and resolves into downbeats, then every beat, as the strip
    // zooms in. Lining the red downbeats up across decks A/B is the "on the grid" read used to beat-match.
    private void RenderBeatComb(
        DrawingContext context, Rect b, double combTop, double combH, bool combAtTop, double start, double span)
    {
        IReadOnlyList<double>? grid = BeatGrid;
        if (grid is not { Count: >= 2 } || span <= 0 || combH <= 0)
            return;

        double stepFraction = grid[1] - grid[0]; // even spacing → one beat
        if (stepFraction <= 0)
            return;
        double beatPx = stepFraction / span * b.Width;
        bool drawBeats = beatPx >= 7.0;
        bool drawBars = beatPx * BeatsPerBar >= 7.0;
        if (!drawBars)
            return; // too zoomed-out to read even downbeats → draw no comb (keeps the overview clean)

        Color beat = (BeatBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0x8E, 0x9A, 0xA8);
        Color down = (DownbeatBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(0xE5, 0x40, 0x3A);
        double combBottom = combTop + combH;
        // Short beat teeth hug the strip's OUTER edge (the side away from the wave): the comb bottom when it
        // sits below the wave, the comb top when it is flipped above it. So a stacked pair's teeth mirror and
        // meet in the middle. Downbeats always run the full comb height, so they line up regardless.
        double beatNear = combAtTop ? combTop : combTop + combH * 0.55;
        double beatFar = combAtTop ? combTop + combH * 0.45 : combBottom;
        var beatPen = new Pen(new ImmutableSolidColorBrush(beat, 0.60), 1.5);
        var downHaloPen = new Pen(new ImmutableSolidColorBrush(down, 0.35), 4.0);
        var downPen = new Pen(new ImmutableSolidColorBrush(down), 2.5);

        int downbeatOffset = DownbeatOffset;
        double end = start + span;
        for (int i = 0; i < grid.Count; i++)
        {
            double fraction = grid[i];
            if (fraction < start || fraction > end)
                continue;
            bool isDownbeat = IsBarDownbeat(i, downbeatOffset, BeatsPerBar);
            if (!isDownbeat && !drawBeats)
                continue;
            double x = (fraction - start) / span * b.Width;
            if (isDownbeat)
            {
                context.DrawLine(downHaloPen, new Point(x, combTop), new Point(x, combBottom));
                context.DrawLine(downPen, new Point(x, combTop), new Point(x, combBottom));
            }
            else
            {
                context.DrawLine(beatPen, new Point(x, beatNear), new Point(x, beatFar));
            }
        }
    }

    /// <summary>
    /// Whether comb line <paramref name="index"/> is a bar downbeat, given the bar-start
    /// <paramref name="offset"/> (which beat of the bar the grid begins on) and the meter
    /// <paramref name="beatsPerBar"/>. True when the line is a whole number of bars from the downbeat, so the
    /// red bar marker lands on the musical "one" rather than on index 0. Pure and public so the placement
    /// unit-tests without a render.
    /// </summary>
    public static bool IsBarDownbeat(int index, int offset, int beatsPerBar)
    {
        if (beatsPerBar < 1)
            return false;
        int folded = ((offset % beatsPerBar) + beatsPerBar) % beatsPerBar;
        return (((index - folded) % beatsPerBar) + beatsPerBar) % beatsPerBar == 0;
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

    // How a column's amplitude maps to a vertical bar. Default: centred (symmetric) bars about the strip
    // midline. Folded: a single-sided bar from a baseline at the comb edge growing AWAY from the comb — the
    // building block of the combined "butterfly" view (upper deck grows up, lower deck grows down).
    private readonly record struct WaveGeometry(double MaxAmp, double Center, double Baseline, double Direction, bool Folded)
    {
        public static WaveGeometry For(double height, bool folded, bool combAtTop)
            => !folded
                ? new WaveGeometry(height / 2 - 2, height / 2, 0, 0, false)
                : combAtTop
                    ? new WaveGeometry(height - 2, 0, 0, +1, true)        // comb on top → bars grow down
                    : new WaveGeometry(height - 2, 0, height, -1, true);  // comb on bottom → bars grow up

        public (Point Top, Point Bottom) Bar(double x, double amp)
            => Folded
                ? (new Point(x, Baseline), new Point(x, Baseline + Direction * amp))
                : (new Point(x, Center - amp), new Point(x, Center + amp));
    }

    // Broadband fallback (no band data): one bar per column, peak-held within the visible window.
    private void RenderWaveform(
        DrawingContext context, Rect b, IReadOnlyList<float> peaks, WaveGeometry geometry, double start, double span)
    {
        const double step = 2.0;

        // Uniform full opacity (no played/ahead split) so the strip reads the same regardless of play
        // position — matches the band render; the playhead alone marks position.
        var pen = new Pen(PlayedBrush, 1.5) { LineCap = PenLineCap.Round };

        for (double x = 1; x < b.Width - 1; x += step)
        {
            if (!ColumnInTrack(x, b.Width, start, span)) continue; // empty past the track ends (centred window)
            double amp = geometry.MaxAmp * Math.Clamp(ColumnPeak(peaks, x, step, b.Width, start, span), 0f, 1f);
            if (amp < 0.5) amp = 0.5; // keep a hairline so silent regions still read as a strip
            (Point top, Point bottom) = geometry.Bar(x, amp);
            context.DrawLine(pen, top, bottom);
        }
    }

    // One band layer (high caps / mid body): a bar per pixel column, peak-held, at full opacity so the deck
    // reads the same regardless of play position (the playhead line alone marks position).
    private void RenderBand(
        DrawingContext context, Rect b, IReadOnlyList<float> band, IBrush brush, WaveGeometry geometry,
        double start, double span)
    {
        const double step = 1.0;

        Color color = (brush as ISolidColorBrush)?.Color ?? Color.FromRgb(0x3D, 0x5C, 0x8F);
        double opacity = (brush as ISolidColorBrush)?.Opacity ?? 1.0;
        var pen = new Pen(new ImmutableSolidColorBrush(color, opacity), 1.0);

        for (double x = 1; x < b.Width - 1; x += step)
        {
            if (!ColumnInTrack(x, b.Width, start, span)) continue; // empty past the track ends (centred window)
            float v = ColumnPeak(band, x, step, b.Width, start, span);
            if (v <= 0.004f) // skip true silence — the broadband hairline already keeps the strip readable
                continue;
            double amp = geometry.MaxAmp * Math.Clamp(v, 0f, 1f);
            (Point top, Point bottom) = geometry.Bar(x, amp);
            context.DrawLine(pen, top, bottom);
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
    private void RenderKickBand(
        DrawingContext context, Rect b, IReadOnlyList<float> kick, WaveGeometry geometry, double start, double span)
    {
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
            if (!ColumnInTrack(x, b.Width, start, span)) continue; // empty past the track ends (centred window)
            float k = ColumnPeak(kick, x, step, b.Width, start, span);
            if (k < floor) continue;

            // Mild gamma so kicks read prominently; halo under a solid core gives glow + a hard edge.
            double amp = geometry.MaxAmp * Math.Pow(Math.Clamp(k, 0f, 1f), 0.8);
            (Point top, Point bottom) = geometry.Bar(x, amp);
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
        var headPen = new Pen(PlayheadBrush, 1.5);
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
