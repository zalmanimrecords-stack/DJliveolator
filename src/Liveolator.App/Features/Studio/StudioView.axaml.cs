using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Liveolator.App.Features.Studio;

public partial class StudioView : UserControl
{
    private bool _initialized;

    // Horizontal clip drag state: the clip being dragged, the pointer X where the drag began (in this
    // control's coordinates, which stay stable as the clip moves), and the clip's start at that moment.
    private StudioClipViewModel? _dragClip;
    private double _dragPressX;
    private double _dragOriginStartSeconds;

    public StudioView() => InitializeComponent();

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_initialized || DataContext is not StudioViewModel vm)
            return;
        _initialized = true;
        await vm.InitializeAsync();
    }

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
}
