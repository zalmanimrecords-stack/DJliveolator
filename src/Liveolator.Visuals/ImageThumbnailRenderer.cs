using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Visuals;

/// <summary>
/// Renders a preview thumbnail for an <see cref="VisualMediaKind.Image"/> asset by decoding the file
/// with SkiaSharp (managed, cross-platform) and scaling it down. Video is not handled here — use
/// <see cref="CompositeVisualThumbnailRenderer"/> to route by kind.
/// </summary>
public sealed class ImageThumbnailRenderer : IVisualThumbnailRenderer
{
    private readonly ILogger<ImageThumbnailRenderer> _logger;

    public ImageThumbnailRenderer(ILogger<ImageThumbnailRenderer>? logger = null)
        => _logger = logger ?? NullLogger<ImageThumbnailRenderer>.Instance;

    public async Task<VisualPreviewFrame?> RenderAsync(
        string filePath, VisualMediaKind kind, int maxEdge, CancellationToken cancellationToken = default)
    {
        if (kind != VisualMediaKind.Image || string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            // Decode off the caller's thread: it is CPU-bound and the caller is typically the UI.
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using FileStream stream = File.OpenRead(filePath);
                return SkiaThumbnail.DecodeScaled(stream, maxEdge);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A bad/missing image must degrade to a placeholder, never crash the library tab.
            _logger.LogWarning(ex, "Could not render image preview for {FilePath}.", filePath);
            return null;
        }
    }
}
