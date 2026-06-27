namespace Liveolator.Core.Library.Doctor;

public enum MediaIdentityKind
{
    Music,
    Visual,
}

public sealed record MediaIdentity(
    string StableId,
    MediaIdentityKind Kind,
    IReadOnlyList<string> Paths,
    string FileName,
    long SizeBytes,
    DateTime LastModifiedUtc,
    string? Sha256,
    MediaAnalysisStatus Status,
    DateTime LastSeenUtc)
{
    public string PrimaryPath => Paths.Count > 0 ? Paths[0] : string.Empty;
}

