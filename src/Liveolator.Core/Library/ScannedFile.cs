namespace Liveolator.Core.Library;

/// <summary>A file discovered during a library scan, with the fields needed for change detection.</summary>
public readonly record struct ScannedFile(string Path, long SizeBytes, DateTime LastModifiedUtc);

/// <summary>Cheap change-detection fingerprint (size + modification time) for incremental scans.</summary>
public readonly record struct FileFingerprint(long SizeBytes, DateTime LastModifiedUtc)
{
    public static FileFingerprint Of(ScannedFile file) => new(file.SizeBytes, file.LastModifiedUtc);
}
