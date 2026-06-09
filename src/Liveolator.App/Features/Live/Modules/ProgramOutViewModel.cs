using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Liveolator.App.Features.Live;
using Liveolator.App.Shell;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>Displays the live compositor feed and launches the clean second-screen output.</summary>
public sealed class ProgramOutViewModel : ViewModelBase, IDisposable
{
    private readonly IVisualPreviewSource? _previewSource;
    private readonly Action<Action> _schedule;
    private readonly object _gate = new();

    private WriteableBitmap? _preview;
    // Double-buffered targets: the compositor reads the bitmap currently bound to the Image while we write
    // the next frame into the OTHER one, then swap the reference. Mutating a single shared bitmap left the
    // Image frozen (an unchanged Source reference is not re-rendered) and could tear mid-write.
    private WriteableBitmap? _bufferA;
    private WriteableBitmap? _bufferB;

    // Latest-frame coalescing: a fast render thread can publish faster than the UI can draw. We keep only
    // the newest frame and schedule at most one pending UI update, so frames never queue up and the
    // preview never falls behind (which read as "stuck + stuttering" before).
    private VisualPreviewFrame? _pendingFrame;
    private bool _updateScheduled;

    public ProgramOutViewModel(
        IVisualStage? visualStage = null,
        IVisualPreviewSource? previewSource = null,
        Action<Action>? schedule = null)
    {
        _previewSource = previewSource;
        _schedule = schedule ?? (action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));
        if (_previewSource is not null)
            _previewSource.PreviewFrameReady += OnPreviewFrameReady;

        ShowVisualsCommand = ReactiveCommand.Create(
            () => visualStage?.Show(),
            Observable.Return(visualStage is not null));
        CanShowVisuals = visualStage is not null;
    }

    public bool CanShowVisuals { get; }
    public ReactiveCommand<Unit, Unit> ShowVisualsCommand { get; }
    public string ResolutionLabel => "Program Out - live visual";

    public WriteableBitmap? Preview
    {
        get => _preview;
        private set => this.RaiseAndSetIfChanged(ref _preview, value);
    }

    public void Dispose()
    {
        if (_previewSource is not null)
            _previewSource.PreviewFrameReady -= OnPreviewFrameReady;
        _bufferA?.Dispose();
        _bufferB?.Dispose();
    }

    private void OnPreviewFrameReady(object? sender, VisualPreviewFrame frame)
    {
        // Fired on the render thread. Stash the newest frame and schedule a single UI drain; if one is
        // already pending, just replace the frame so the UI always paints the freshest one available.
        lock (_gate)
        {
            _pendingFrame = frame;
            if (_updateScheduled)
                return;
            _updateScheduled = true;
        }

        _schedule(DrainLatestFrame);
    }

    private void DrainLatestFrame()
    {
        VisualPreviewFrame? frame;
        lock (_gate)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
            _updateScheduled = false;
        }

        if (frame is not null)
            ApplyFrame(frame);
    }

    private void ApplyFrame(VisualPreviewFrame frame)
    {
        var size = new PixelSize(frame.Width, frame.Height);
        WriteableBitmap target = NextBuffer(size);

        using (ILockedFramebuffer framebuffer = target.Lock())
            Marshal.Copy(frame.RgbaPixels, 0, framebuffer.Address, frame.RgbaPixels.Length);

        // Swap the bound reference so Image.Source actually changes and the control repaints.
        Preview = target;
    }

    // Returns the back buffer (the one not currently shown), recreating both on a size change.
    private WriteableBitmap NextBuffer(PixelSize size)
    {
        if (_bufferA is null || _bufferA.PixelSize != size)
        {
            _bufferA?.Dispose();
            _bufferB?.Dispose();
            _bufferA = CreateBuffer(size);
            _bufferB = CreateBuffer(size);
        }

        return ReferenceEquals(Preview, _bufferA) ? _bufferB! : _bufferA!;
    }

    private static WriteableBitmap CreateBuffer(PixelSize size) => new(
        size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
}
