using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class LibraryDoctorTests
{
    private static readonly DateTime T = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Scan_ReportsMissingBrokenLowConfidenceAndDuplicates()
    {
        var tracks = new[]
        {
            Track("/music/missing.mp3", MediaAnalysisStatus.Ok, exists: false),
            Track("/music/broken.mp3", MediaAnalysisStatus.Failed),
            Track("/music/weak.mp3", MediaAnalysisStatus.PartiallyAnalyzed, bpmConfidence: 0.2),
            Track("/music/a/song.mp3", MediaAnalysisStatus.Ok, size: 500, sha: "abc"),
            Track("/music/b/song-copy.mp3", MediaAnalysisStatus.Ok, size: 500, sha: "abc"),
        };
        var identities = tracks.Select(t =>
            MediaIdentityBuilder.FromEntry(t, MediaIdentityKind.Music, T, new Dictionary<string, string?>
            {
                [t.File.Path] = t.Metadata?.Comment,
            })).ToList();
        var doctor = new LibraryDoctor(
            new FakeFileProbe(tracks.Where(t => t.File.Path != "/music/missing.mp3").Select(t => t.File.Path)),
            new FakeFolderProbe("/music"));

        LibraryDoctorReport report = doctor.Scan(tracks, Array.Empty<Liveolator.Core.Library.Visual.VisualAsset>(), new[] { "/music" }, Array.Empty<string>(), identities);

        Assert.Contains(report.Issues, i => i.Kind == LibraryIssueKind.MissingFile);
        Assert.Contains(report.Issues, i => i.Kind == LibraryIssueKind.BrokenAnalysis);
        Assert.Contains(report.Issues, i => i.Kind == LibraryIssueKind.LowConfidenceAnalysis);
        LibraryIssue duplicate = Assert.Single(report.Issues.Where(i => i.Kind == LibraryIssueKind.DuplicateCandidate));
        Assert.Equal(LibraryRepairConfidence.High, duplicate.Confidence);
    }

    [Fact]
    public void DuplicateFinder_UsesHashAsHighConfidence_AndNameSizeAsLowConfidence()
    {
        var entries = new[]
        {
            Entry("/a/one.mp3", 100),
            Entry("/b/two.mp3", 100),
            Entry("/c/fallback.mp3", 200),
            Entry("/d/fallback.mp3", 200),
        };
        var identities = new[]
        {
            Identity(entries[0], "same"),
            Identity(entries[1], "same"),
        };

        IReadOnlyList<DuplicateGroup<FakeEntry>> groups = DuplicateFinder.FindWithIdentities(entries, identities);

        Assert.Equal(2, groups.Count);
        Assert.Equal(LibraryRepairConfidence.High, groups[0].Confidence);
        Assert.Equal(LibraryRepairConfidence.Low, groups[1].Confidence);
    }

    private static MusicTrack Track(
        string path,
        MediaAnalysisStatus status,
        bool exists = true,
        long size = 100,
        double bpmConfidence = 0.9,
        string? sha = null)
        => new(
            new ScannedFile(path, size, T),
            new BpmResult(124, bpmConfidence),
            new MusicalKey(0, KeyMode.Major, "8B", 0.9),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            status,
            status == MediaAnalysisStatus.Failed ? "decode failed" : null,
            TrackMetadata.Empty with { Comment = sha });

    private sealed record FakeEntry(ScannedFile File, MediaAnalysisStatus Status = MediaAnalysisStatus.Ok)
        : IMediaEntry;

    private static FakeEntry Entry(string path, long size)
        => new(new ScannedFile(path, size, T));

    private static MediaIdentity Identity(FakeEntry entry, string sha)
        => new("id-" + entry.File.Path, MediaIdentityKind.Music, new[] { entry.File.Path },
            Path.GetFileName(entry.File.Path), entry.File.SizeBytes, entry.File.LastModifiedUtc,
            sha, entry.Status, T);

    private sealed class FakeFileProbe : IFileExistenceProbe
    {
        private readonly HashSet<string> _paths;
        public FakeFileProbe(IEnumerable<string> paths) => _paths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => _paths.Contains(path);
    }

    private sealed class FakeFolderProbe : IFolderExistenceProbe
    {
        private readonly HashSet<string> _folders;
        public FakeFolderProbe(params string[] folders) => _folders = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        public bool Exists(string folder) => _folders.Contains(folder);
    }
}

