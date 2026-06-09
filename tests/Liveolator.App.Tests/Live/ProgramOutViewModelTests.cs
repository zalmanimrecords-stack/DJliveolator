using Avalonia.Headless.XUnit;
using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.App.Tests.Live;

public sealed class ProgramOutViewModelTests
{
    [AvaloniaFact]
    public void FrameReady_SchedulesAUiUpdate_AndAppliesIt()
    {
        var source = new FakePreviewSource();
        var scheduled = new List<Action>();
        using var vm = new ProgramOutViewModel(previewSource: source, schedule: scheduled.Add);

        source.Raise(Frame(2, 2, marker: 11));
        Assert.Single(scheduled); // one UI post queued, not run yet

        scheduled[0]();
        Assert.NotNull(vm.Preview);
        Assert.Equal(11, FirstByte(vm.Preview!));
    }

    [AvaloniaFact]
    public void BurstOfFrames_CoalescesToOneUpdate_RenderingTheLatest()
    {
        var source = new FakePreviewSource();
        var scheduled = new List<Action>();
        using var vm = new ProgramOutViewModel(previewSource: source, schedule: scheduled.Add);

        // Three frames arrive before the UI thread drains any of them.
        source.Raise(Frame(2, 2, marker: 1));
        source.Raise(Frame(2, 2, marker: 2));
        source.Raise(Frame(2, 2, marker: 3));

        // Coalesced: only ONE UI post is queued no matter how many frames arrived (no backpressure).
        Assert.Single(scheduled);

        scheduled[0]();
        // The freshest frame is shown; the two stale ones are dropped, not queued behind it.
        Assert.Equal(3, FirstByte(vm.Preview!));
    }

    [AvaloniaFact]
    public void ConsecutiveFrames_SwapBitmapReference_SoTheImageRepaints()
    {
        var source = new FakePreviewSource();
        var scheduled = new List<Action>();
        using var vm = new ProgramOutViewModel(previewSource: source, schedule: scheduled.Add);

        source.Raise(Frame(2, 2, marker: 5));
        scheduled[^1]();
        object? first = vm.Preview;

        source.Raise(Frame(2, 2, marker: 6));
        scheduled[^1]();
        object? second = vm.Preview;

        Assert.NotNull(first);
        Assert.NotNull(second);
        // Same dimensions but a different bitmap instance, so Image.Source actually changes and repaints
        // (mutating one shared WriteableBitmap left the preview frozen).
        Assert.NotSame(first, second);
        Assert.Equal(6, FirstByte(vm.Preview!));
    }

    private static VisualPreviewFrame Frame(int w, int h, byte marker)
    {
        byte[] px = new byte[w * h * 4];
        px[0] = marker;
        return new VisualPreviewFrame(w, h, px);
    }

    private static unsafe byte FirstByte(Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        using Avalonia.Platform.ILockedFramebuffer fb = bitmap.Lock();
        return ((byte*)fb.Address)[0];
    }

    private sealed class FakePreviewSource : IVisualPreviewSource
    {
        public event EventHandler<VisualPreviewFrame>? PreviewFrameReady;

        public void Raise(VisualPreviewFrame frame) => PreviewFrameReady?.Invoke(this, frame);
    }
}
