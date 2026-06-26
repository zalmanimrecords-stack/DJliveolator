namespace Liveolator.Core.Update;

/// <summary>
/// Reports the version of the build that is currently running, so the startup check can compare it to the
/// latest published manifest. A seam because the running version comes from assembly metadata, which is a
/// host concern (the App binding reads it); the comparison logic stays pure and testable.
/// </summary>
public interface IInstalledVersionProvider
{
    /// <summary>The running build's version string (e.g. <c>0.1.4</c>).</summary>
    string CurrentVersion { get; }
}
