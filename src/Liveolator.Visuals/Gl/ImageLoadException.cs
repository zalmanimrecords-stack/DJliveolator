namespace Liveolator.Visuals.Gl;

/// <summary>
/// Raised when an image source cannot be loaded/decoded into a GPU texture. The engine catches it
/// and renders the layer transparent rather than crashing the render loop (doc 08 error handling).
/// </summary>
public sealed class ImageLoadException : Exception
{
    public ImageLoadException(string message) : base(message) { }

    public ImageLoadException(string message, Exception innerException) : base(message, innerException) { }
}
