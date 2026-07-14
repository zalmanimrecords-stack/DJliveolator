namespace Liveolator.Visuals.Tests;

/// <summary>
/// A temp file that deletes itself on <see cref="Dispose"/>, used to feed hand-crafted image
/// headers to the probe. Deletion is best-effort: a leaked temp file must never fail a test,
/// so cleanup errors are swallowed deliberately (not a silent failure of any production flow).
/// </summary>
public sealed class TempFile : IDisposable
{
    public string Path { get; }

    private TempFile(string path) => Path = path;

    public static TempFile WithBytes(string extension, byte[] contents)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"liveolator-visuals-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, contents);
        return new TempFile(path);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (IOException)
        {
            // Temp-file cleanup is best-effort; the OS reclaims the temp directory regardless.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: never let teardown of a test fixture surface as a test failure.
        }
    }
}
