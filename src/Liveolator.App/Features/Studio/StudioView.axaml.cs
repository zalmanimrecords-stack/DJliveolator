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
    private const double DragThresholdPx = 5;

    private bool _initialized;

    // Horizontal clip drag state (move a clip in time).
    private StudioClipViewModel? _dragClip;
    private double _dragPressX;
    private double _dragOriginStartSeconds;

    // Library drag-source state (drag a track onto a lane).
    private Point _libPressPoint;
    private TrackRowViewModel? _libPressRow;
    private bool _libDragging;

    public StudioView() => InitializeComponent();

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Wire the lane area as a drop target once the template is realized.
        DragDrop.SetAllowDrop(LanesItems, true);
        LanesItems.AddHandler(DragDrop.DragOverEvent, OnLaneDragOver);
        LanesItems.AddHandler(DragDrop.DropEvent, OnLaneDrop);

        if (_initialized || DataContext is not StudioViewModel vm)
            return;
        _initialized = true;
        await vm.InitializeAsync();
    }

    // --- clip drag (move in time) ---

    private void OnClipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not StudioClipViewModel clip ||
            DataContext is not StudioViewModel vm)
            return;

        vm.SelectedClip = clip;
        _dragClip = clip;
        _dragPressX = e.GetPosition(this).X;
        _dragOriginStartSeconds = clip.TimelineStartSeconds;
        e.Pointer.Capture(control);
    }

    private void OnClipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragClip is null || DataContext is not StudioViewModel vm)
            return;

        double dx = e.GetPosition(this).X - _dragPressX;
        double deltaSeconds = vm.PixelsPerSecond > 0 ? dx / vm.PixelsPerSecond : 0;
        double target = _dragOriginStartSeconds + deltaSeconds;
        _dragClip.TimelineStartSeconds = TimelineMath.Snap(target, TimelineMath.BeatSeconds(vm.Bpm));
    }

    private void OnClipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragClip = null;
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
        int deck = System.Math.Clamp((int)(p.Y / LaneRowHeightPx), 0, 3);
        double timeSeconds = TimelineMath.SecondsFromX(p.X - StudioViewModel.LaneGutterPx, vm.PixelsPerSecond);
        vm.AddClipAt(path, deck, timeSeconds);
        e.Handled = true;
    }
}
