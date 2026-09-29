using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist.LocalCopy;
using Xunit;
using PlaylistRecord = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Core.Tests.Playlist.LocalCopy;

public class LocalCopyPlannerTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string Nas = @"\\nas\music";
    private const string Gig = @"C:\Gig";

    [Fact]
    public void Plan_UncTrack_LandsUnderTheFolderLeafWithItsSubfoldersKept()
    {
        LocalCopyItem item = Single(PlanOf(new[] { $@"{Nas}\Techno\2024\a.mp3" }));

        Assert.Equal(LocalCopyStep.Copy, item.Step);
        Assert.Equal(@"C:\Gig\music\Techno\2024\a.mp3", item.DestinationPath);
    }

    [Fact]
    public void Plan_MappedDriveRoot_UsesTheDriveLetterAsTheLeaf()
    {
        LocalCopyItem item = Single(LocalCopyPlanner.Plan(
            Playlist(@"M:\a.mp3"), Catalog(@"M:\a.mp3"), new[] { @"M:\" }, Gig, _ => null));

        Assert.Equal(@"C:\Gig\M\a.mp3", item.DestinationPath);
    }

    [Fact]
    public void Plan_UnixDestination_UsesForwardSlashes()
    {
        LocalCopyItem item = Single(LocalCopyPlanner.Plan(
            Playlist($@"{Nas}\x\a.mp3"), Catalog($@"{Nas}\x\a.mp3"), new[] { Nas }, "/Users/dj/Gig", _ => null));

        Assert.Equal("/Users/dj/Gig/music/x/a.mp3", item.DestinationPath);
    }

    [Fact]
    public void Plan_DestinationWithTheSameSize_IsAlreadyPresent_AndADifferentSizeIsRecopied()
    {
        string same = $@"{Nas}\same.mp3", changed = $@"{Nas}\changed.mp3";
        IReadOnlyList<LocalCopyItem> items = LocalCopyPlanner.Plan(
            Playlist(same, changed), Catalog(same, changed), new[] { Nas }, Gig,
            dest => new ScannedFile(dest, dest.EndsWith("same.mp3") ? 1000 : 5, T));

        Assert.Equal(LocalCopyStep.AlreadyPresent, items[0].Step);
        Assert.Equal(LocalCopyStep.Copy, items[1].Step);
    }

    [Fact]
    public void Plan_UncataloguedPath_IsReportedNotDropped()
    {
        IReadOnlyList<LocalCopyItem> items = LocalCopyPlanner.Plan(
            Playlist($@"{Nas}\unknown.mp3"), Catalog(), new[] { Nas }, Gig, _ => null);

        Assert.Equal(LocalCopyStep.NotCatalogued, Single(items).Step);
    }

    [Fact]
    public void Plan_TwoFoldersWithTheSameLeaf_FlagTheClashInsteadOfOverwriting()
    {
        string a = @"\\nas\music\a.mp3", b = @"D:\music\a.mp3";
        IReadOnlyList<LocalCopyItem> items = LocalCopyPlanner.Plan(
            Playlist(a, b), Catalog(a, b), new[] { @"\\nas\music", @"D:\music" }, Gig, _ => null);

        Assert.Equal(LocalCopyStep.Copy, items[0].Step);
        Assert.Equal(LocalCopyStep.NameClash, items[1].Step);
    }

    [Fact]
    public void Plan_RepeatedEntry_IsPlannedOnce()
    {
        string a = $@"{Nas}\a.mp3";

        Assert.Single(PlanOf(new[] { a, a }));
    }

    [Fact]
    public void Plan_TrackAlreadyUnderTheDestination_StaysWhereItIs()
    {
        string local = @"C:\Gig\music\a.mp3";
        LocalCopyItem item = Single(LocalCopyPlanner.Plan(
            Playlist(local), Catalog(local), new[] { Nas, Gig }, Gig, _ => null));

        Assert.Equal(LocalCopyStep.AlreadyPresent, item.Step);
        Assert.Equal(local, item.DestinationPath);
    }

    [Fact]
    public void Plan_TrackOutsideEveryScanFolder_LandsFlatInTheDestination()
    {
        string loose = @"E:\loose\a.mp3";
        LocalCopyItem item = Single(LocalCopyPlanner.Plan(
            Playlist(loose), Catalog(loose), new[] { Nas }, Gig, _ => null));

        Assert.Equal(@"C:\Gig\a.mp3", item.DestinationPath);
    }

    [Fact]
    public void BytesToCopy_CountsOnlyTheFilesThatWillBeCopied()
    {
        string copy = $@"{Nas}\copy.mp3", present = $@"{Nas}\present.mp3";
        IReadOnlyList<LocalCopyItem> items = LocalCopyPlanner.Plan(
            Playlist(copy, present), Catalog(copy, present), new[] { Nas }, Gig,
            dest => dest.EndsWith("present.mp3") ? new ScannedFile(dest, 1000, T) : null);

        Assert.Equal(1000, LocalCopyPlanner.BytesToCopy(items));
    }

    private static IReadOnlyList<LocalCopyItem> PlanOf(string[] paths)
        => LocalCopyPlanner.Plan(Playlist(paths), Catalog(paths), new[] { Nas }, Gig, _ => null);

    private static LocalCopyItem Single(IReadOnlyList<LocalCopyItem> items) => Assert.Single(items);

    private static PlaylistRecord Playlist(params string[] paths) => new("Friday", paths);

    private static Dictionary<string, MusicTrack> Catalog(params string[] paths)
        => paths.Distinct().ToDictionary(p => p, Track, StringComparer.OrdinalIgnoreCase);

    internal static MusicTrack Track(string path) => new(
        new ScannedFile(path, 1000, T),
        new BpmResult(124.0, Confidence: 1.0, FirstBeatSeconds: 0.5),
        Key: null,
        Duration: TimeSpan.FromMinutes(4),
        Cues: TrackCues.None,
        Status: MediaAnalysisStatus.Ok,
        Error: null);
}
