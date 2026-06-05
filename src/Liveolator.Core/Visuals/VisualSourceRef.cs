namespace Liveolator.Core.Visuals;

/// <summary>
/// A serializable reference to a layer's texture source. References by path/clip-id/camera-id (not
/// a live handle) so scenes survive asset-folder changes and a missing asset can degrade gracefully
/// (doc 08/13).
/// </summary>
/// <param name="Kind">Image, video clip, or camera.</param>
/// <param name="Reference">Image path, video clip id, or camera/capture id.</param>
public sealed record VisualSourceRef(VisualSourceKind Kind, string Reference);
