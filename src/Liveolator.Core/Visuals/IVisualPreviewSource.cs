namespace Liveolator.Core.Visuals;

public sealed record VisualPreviewFrame(int Width, int Height, byte[] RgbaPixels);

/// <summary>Publishes occasional compositor frames for the in-app Program Out monitor.</summary>
public interface IVisualPreviewSource
{
    event EventHandler<VisualPreviewFrame>? PreviewFrameReady;
}
