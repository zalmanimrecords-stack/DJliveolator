using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Liveolator.App.Features.Libraries;

namespace Liveolator.App.Features.Studio;

public partial class StudioView : UserControl
{
    // DnD payload: the dragged library track's file path.
    private const string TrackPathFormat = "liveolator/track-path";

    // One lane row's pixel height: the lane Grid (62) + its bottom margin (4). Used to map a drop's
    // Y to a deck index. Must match StudioView.axaml's lane template.
    private const double LaneRowHeightPx = 66;

    // STUDIO has two lanes (A=0, B=1); a drop's Y maps into this range.
    private const int MaxDeckIndex = 1;
    private const double DragThresholdPx = 5;

    private bool _initialized;

    // Horizontal clip drag state (move a clip in time, or trim either edge).
    private StudioClipViewModel? _dragClip;
    private bool _dragMoved;
    private double _dragPressX;
    private double _dragOriginStartSeconds;
    private double _lastTrimX;        // last pointer X for incremental edge-trim deltas
    private ClipDragMode _clipDragMode;

    // How a clip pointer-press is interpreted: a top corner adjusts a fade, an edge trims, elsewhere moves.
    private enum ClipDragMode { Move, TrimStart, TrimEnd, FadeIn, FadeOut }

    // Pointer distance from a clip edge that counts as grabbing that edge's trim handle.
    private const double EdgeGrabPx = 7;
    // The top strip / corner width within which a grab adjusts a fade instead of trimming.
    private const double FadeZoneTopPx = 16;
    private const double FadeCornerPx = 12;

    // Library drag-source state (drag a track onto a lane).
    private Point _libPressPoint;
    private TrackRowViewModel? _libPressRow;
    private bool _libDragging;

    public StudioView() => InitializeComponent();

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_initialized)
            return;
        _initialized = true;

        // Lane area = drop target for library tracks.
        DragDrop.SetAllowDrop(LanesItems, true);
        LanesItems.AddHandler(DragDrop.DragOverEvent, OnLaneDragOver);
        LanesItems.AddHandler(DragDrop.DropEvent, OnLaneDrop);

        // Library = drag source. Tunnel + handledEventsToo so we still see the press/move even though the
        // ListBox marks PointerPressed handled for its own selection (otherwise the drag never starts).
        LibraryList.AddHandler(InputElement.PointerPressedEvent, OnLibraryPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LibraryList.AddHandler(InputElement.PointerMovedEvent, OnLibraryPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // Wheel / trackpad scroll over the timeline zooms (Tunnel so it pre-empts the ScrollViewer's pan).
        LanesScroll.AddHandler(InputElement.PointerWheelChangedEvent, OnTimelineWheel, RoutingStrategies.Tunnel);

        // Grab-to-pan: drag the timeline left/right with the mouse. Middle button pans anywhere; left button
        // pans on empty lane background (a clip press is marked handled, so it drags the clip instead).
        // handledEventsToo so we still see a clip's handled press and can correctly decline to pan over it.
        LanesScroll.AddHandler(InputElement.PointerPressedEvent, OnTimelinePanPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        LanesScroll.AddHandler(InputElement.PointerMovedEvent, OnTimelinePanMoved, RoutingStrategies.Bubble);
        LanesScroll.AddHandler(InputElement.PointerReleasedEvent, OnTimelinePanReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);

        if (DataContext is StudioViewModel vm)
            await vm.InitializeAsync();
    }

    private const double MinZoom = 2;
    private const double MaxZoom = 200;

    // Zoom the timeline around the cursor: change pixels-per-second and shift the horizontal scroll so the
    // time under the pointer stays put (standard DAW wheel-zoom). Works with a mouse wheel or trackpad scroll.
    private void OnTimelineWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not StudioViewModel vm)
            return;

        double oldPps = vm.PixelsPerSecond;
        double factor = e.Delta.Y >= 0 ? 1.2 : 1.0 / 1.2;
        double newPps = System.Math.Clamp(oldPps * factor, MinZoom, MaxZoom);
        e.Handled = true;
        if (System.Math.Abs(newPps - oldPps) < 1e-9)
            return;

        // LanesItems and LanesScroll are the timeline CONTENT (the lane headers are a separate fixed
        // column), so the cursor's X is already measured from the content's time-0 origin — no gutter
        // offset.
        double timeAtCursor = System.Math.Max(0, e.GetPosition(LanesItems).X) / oldPps;
        double viewportCursorX = e.GetPosition(LanesScroll).X;

        vm.PixelsPerSecond = newPps;

        double newContentX = timeAtCursor * newPps;
        LanesScroll.Offset = new Vector(System.Math.Max(0, newContentX - viewportCursorX), LanesScroll.Offset.Y);
    }

    // --- timeline pan (grab-drag to scroll the arrangement horizontally) ---

    private bool _panning;
    private double _panPressX;       // pointer X (this control's space) at the grab start
    private double _panStartOffsetX; // horizontal scroll offset at the grab start

    private void OnTimelinePanPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        // Middle button pans anywhere; left button pans only on empty background — a clip press is marked
        // handled (clip drag), and an envelope-edit gesture owns the left button while Automation mode is on.
        bool middle = props.IsMiddleButtonPressed;
        bool leftOnEmpty = props.IsLeftButtonPressed && !e.Handled;
        if (!middle && !leftOnEmpty)
            return;

        _panning = true;
        _panPressX = e.GetPosition(this).X;
        _panStartOffsetX = LanesScroll.Offset.X;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        e.Pointer.Capture(LanesScroll);
        e.Handled = true;
    }

    private void OnTimelinePanMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning)
            return;

        double dx = e.GetPosition(this).X - _panPressX;
        double maxOffset = System.Math.Max(0, LanesScroll.Extent.Width - LanesScroll.Viewport.Width);
        double target = System.Math.Clamp(_panStartOffsetX - dx, 0, maxOffset);
        LanesScroll.Offset = new Vector(target, LanesScroll.Offset.Y);
    }

    private void OnTimelinePanReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning)
            return;

        _panning = false;
        Cursor = Cursor.Default;
        e.Pointer.Capture(null);
    }

    // --- clip drag (move in time) ---

    private void OnClipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only the left button drags a clip; middle/right is reserved for panning the timeline.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (sender is not Control control || control.DataContext is not StudioClipViewModel clip ||
            DataContext is not StudioViewModel vm)
            return;

        vm.SelectedClip = clip;
        _dragClip = clip;
        _dragMoved = false;
        _dragPressX = e.GetPosition(this).X;
        _dragOriginStartSeconds = clip.TimelineStartSeconds;
        _lastTrimX = _dragPressX;

        // A top corner adjusts a fade; an edge (below the top strip) trims; the body moves the clip.
        Point local = e.GetPosition(control);
        double width = control.Bounds.Width;
        bool topZone = local.Y <= FadeZoneTopPx;
        _clipDragMode =
            topZone && local.X <= FadeCornerPx ? ClipDragMode.FadeIn
            : topZone && local.X >= width - FadeCornerPx ? ClipDragMode.FadeOut
            : local.X <= EdgeGrabPx ? ClipDragMode.TrimStart
            : local.X >= width - EdgeGrabPx ? ClipDragMode.TrimEnd
            : ClipDragMode.Move;

        e.Pointer.Capture(control);
        e.Handled = true; // a clip press is a clip drag — stop the timeline-pan handler from also firing
    }

    private void OnClipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragClip is null || DataContext is not StudioViewModel vm)
            return;

        if (_clipDragMode == ClipDragMode.Move)
        {
            double dx = e.GetPosition(this).X - _dragPressX;
            double deltaSeconds = vm.PixelsPerSecond > 0 ? dx / vm.PixelsPerSecond : 0;
            double target = TimelineMath.Snap(_dragOriginStartSeconds + deltaSeconds, TimelineMath.BeatSeconds(vm.Bpm));

            // Record one undo snapshot on the first real position change of the gesture (not on a plain
            // click-to-select), then suppress per-move pushes until release.
            if (!_dragMoved && target != _dragClip.TimelineStartSeconds)
            {
                _dragClip.BeginDrag();
                _dragMoved = true;
            }
            _dragClip.TimelineStartSeconds = target;
            return;
        }

        // Edge trim / corner fade: apply the incremental timeline delta since the last move (BeginDrag once,
        // so the whole gesture is one undo step). The clip VM clamps and honours the warp factor.
        double currentX = e.GetPosition(this).X;
        double delta = vm.PixelsPerSecond > 0 ? (currentX - _lastTrimX) / vm.PixelsPerSecond : 0;
        _lastTrimX = currentX;
        if (delta == 0)
            return;
        if (!_dragMoved)
        {
            _dragClip.BeginDrag();
            _dragMoved = true;
        }
        switch (_clipDragMode)
        {
            case ClipDragMode.TrimStart: _dragClip.DragStartEdge(delta); break;
            case ClipDragMode.TrimEnd: _dragClip.DragEndEdge(delta); break;
            case ClipDragMode.FadeIn: _dragClip.DragFadeIn(delta); break;
            // Tail fade grows as the corner is dragged inward (leftward), so invert the delta.
            case ClipDragMode.FadeOut: _dragClip.DragFadeOut(-delta); break;
        }
    }

    private void OnClipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragClip?.EndDrag();
        _dragClip = null;
        _dragMoved = false;
        e.Pointer.Capture(null);
    }

    // --- library track drag source ---

    private void OnLibraryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _libPressPoint = e.GetPosition(this);
        _libPressRow = (e.Source as Control)?.DataContext as TrackRowViewModel;
        _libDragging = false;
    }

    private async void OnLibraryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_libDragging || _libPressRow is not { } row)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Point delta = e.GetPosition(this) - _libPressPoint;
        if (System.Math.Abs(delta.X) + System.Math.Abs(delta.Y) < DragThresholdPx)
            return;

        _libDragging = true;
        var data = new DataObject();
        data.Set(TrackPathFormat, row.Track.File.Path);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        _libDragging = false;
        _libPressRow = null;
    }

    // --- lane drop target ---

    private void OnLaneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(TrackPathFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLaneDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not StudioViewModel vm || e.Data.Get(TrackPathFormat) is not string path)
            return;

        Point p = e.GetPosition(LanesItems);
        int deck = System.Math.Clamp((int)(p.Y / LaneRowHeightPx), 0, MaxDeckIndex);
        // p.X is relative to the clip content (LanesItems), whose origin is time-0 — no gutter offset.
        double timeSeconds = TimelineMath.SecondsFromX(p.X, vm.PixelsPerSecond);
        vm.AddClipAt(path, deck, timeSeconds);
        e.Handled = true;
    }
}
