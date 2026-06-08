namespace Liveolator.Core.Visuals;

/// <summary>
/// A serializable reference to a layer's texture source. References by path/clip-id/camera-id (not
/// a live handle) so scenes survive asset-folder changes and a missing asset can degrade gracefully
/// (doc 08/13).
/// </summary>
/// <param name="Kind">Image, video clip, camera, or generator.</param>
/// <param name="Reference">
/// Meaning depends on <paramref name="Kind"/>: an image path (<see cref="VisualSourceKind.Image"/>), a
/// video clip id (<see cref="VisualSourceKind.VideoClip"/>), a camera/capture id
/// (<see cref="VisualSourceKind.Camera"/>), or a generator effect id
/// (<see cref="VisualSourceKind.Generator"/>) resolved via the <see cref="IVisualEffectRegistry"/>.
/// </param>
public sealed record VisualSourceRef(VisualSourceKind Kind, string Reference);
