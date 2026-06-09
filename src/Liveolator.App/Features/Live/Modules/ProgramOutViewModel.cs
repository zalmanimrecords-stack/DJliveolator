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
    private WriteableBitmap? _preview;

    public ProgramOutViewModel(
        IVisualStage? visualStage = null,
        IVisualPreviewSource? previewSource = null)
    {
        _previewSource = previewSource;
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
        Preview?.Dispose();
    }

    private void OnPreviewFrameReady(object? sender, VisualPreviewFrame frame)
        => Dispatcher.UIThread.Post(() => ApplyFrame(frame));

    private void ApplyFrame(VisualPreviewFrame frame)
    {
        if (Preview?.PixelSize != new PixelSize(frame.Width, frame.Height))
        {
            Preview?.Dispose();
            Preview = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Opaque);
        }

        using ILockedFramebuffer framebuffer = Preview!.Lock();
        Marshal.Copy(frame.RgbaPixels, 0, framebuffer.Address, frame.RgbaPixels.Length);
        this.RaisePropertyChanged(nameof(Preview));
    }
}
