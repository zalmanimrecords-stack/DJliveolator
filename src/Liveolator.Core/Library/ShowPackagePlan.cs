namespace Liveolator.Core.Library.Packaging;

public enum ShowPackageItemKind
{
    Audio,
    Visual,
    Preset,
    Metadata,
    Project,
}

public sealed record ShowPackageItem(
    ShowPackageItemKind Kind,
    string SourcePath,
    string RelativeTargetPath,
    long? SizeBytes = null);

public sealed record ShowPackageManifest(
    string Name,
    DateTime CreatedUtc,
    IReadOnlyList<ShowPackageItem> Items);

public sealed record ShowPackagePlan(
    string Name,
    string TargetFolder,
    IReadOnlyList<ShowPackageItem> Items)
{
    public ShowPackageManifest ToManifest(DateTime createdUtc)
        => new(Name, createdUtc, Items);
}

