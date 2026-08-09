using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

/// <summary>
/// A scan of some folders must leave the rest of the catalog alone. Diffing the enumerated files against
/// the WHOLE catalog made every track outside the scanned folders look deleted, which forced every caller
/// to re-walk the union of every folder it had ever scanned — turning a ten-file request into a whole-
/// library pass (issue #3).
/// </summary>
public class MediaLibraryScopedScanTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int Sr = 44100;
    private const string Curated = "C:/music/curated";
    private const string Other = "C:/music/other";

    [Fact]
    public async Task Scan_OfOneFolder_KeepsTheTracksCataloguedUnderAnother()
    {
        MusicLibrary library = Library(out FolderScopedEnumerator enumerator);
        await library.ScanAsync(new[] { Curated, Other });
        Assert.Equal(2, library.Count);

        await library.ScanAsync(new[] { Curated });

        Assert.Equal(2, library.Count);
        Assert.NotNull(library.TryGet($"{Other}/theirs.mp3"));
    }

    [Fact]
    public async Task Scan_OfOneFolder_StillDropsAFileDeletedFromThatFolder()
    {
        MusicLibrary library = Library(out FolderScopedEnumerator enumerator);
        await library.ScanAsync(new[] { Curated, Other });

        enumerator.Files.RemoveAll(f => f.Path == $"{Curated}/mine.mp3");
        await library.ScanAsync(new[] { Curated });

        Assert.Null(library.TryGet($"{Curated}/mine.mp3"));
        Assert.NotNull(library.TryGet($"{Other}/theirs.mp3"));
    }

    [Fact]
    public async Task Scan_OfOneFolder_DoesNotReanalyzeTheOtherFolder()
    {
        MusicLibrary library = Library(out FolderScopedEnumerator enumerator, out MapAudioDecoder decoder);
        await library.ScanAsync(new[] { Curated, Other });
        int before = decoder.DecodeCalls[$"{Other}/theirs.mp3"];

        await library.ScanAsync(new[] { Curated });

        Assert.Equal(before, decoder.DecodeCalls[$"{Other}/theirs.mp3"]);
    }

    private static MusicLibrary Library(out FolderScopedEnumerator enumerator)
        => Library(out enumerator, out _);

    private static MusicLibrary Library(out FolderScopedEnumerator enumerator, out MapAudioDecoder decoder)
    {
        enumerator = new FolderScopedEnumerator(
            new ScannedFile($"{Curated}/mine.mp3", 1000, T),
            new ScannedFile($"{Other}/theirs.mp3", 1000, T));
        decoder = new MapAudioDecoder(new()
        {
            [$"{Curated}/mine.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            [$"{Other}/theirs.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
        });
        return new MusicLibrary(enumerator, decoder);
    }

    /// <summary>Enumerates only the files under the folders it is asked for — what a real one does, and
    /// what the shared <see cref="FakeFileEnumerator"/> deliberately ignores.</summary>
    private sealed class FolderScopedEnumerator : IFileEnumerator
    {
        public List<ScannedFile> Files { get; }

        public FolderScopedEnumerator(params ScannedFile[] files) => Files = files.ToList();

        public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
            => Files.Where(f => folders.Any(folder => FolderScope.IsUnder(f.Path, folder)));
    }
}
