namespace Liveolator.Core.Persistence;

/// <summary>
/// The startup check of the persistence root. <paramref name="Warning"/> is set when the root is not
/// where writes really land (Windows package virtualization), which means this process works on a
/// private copy of the library instead of the one the App normally uses.
/// </summary>
public sealed record DataRootStatus(string? Warning)
{
    public static DataRootStatus Healthy { get; } = new((string?)null);
}
