using System.Collections.Generic;
using System.IO;
using Liveolator.Core.Analysis.Stems;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The local stem cache layout, manifest persistence, and cache hit/miss logic — pure filesystem,
/// no Python or network (doc 32 §2.3).
/// </summary>
public class StemStoreTests
{
    private const string Source = "/music/artist - track.mp3";

    [Fact]
    public void FolderFor_IsStableForTheSamePath_AndDiffersAcrossPaths()
    {
        var store = new StemStore(Path.GetTempPath());
        Assert.Equal(store.FolderFor(Source), store.FolderFor(Source));
        Assert.NotEqual(store.FolderFor(Source), store.FolderFor("/music/other.mp3"));
    }

    [Fact]
    public void TryLoad_NoManifest_IsMiss()
    {
        using var dir = new TempDirectory();
        var store = new StemStore(dir.Path);
        Assert.Null(store.TryLoad(Source));
    }

    [Fact]
    public void SaveThenTryLoad_WhenStemFilesExist_IsHit()
    {
        using var dir = new TempDirectory();
        var store = new StemStore(dir.Path);
        StemSet set = MakeSetWithRealFiles(store, dir);

        store.Save(set);
        StemSet? loaded = store.TryLoad(Source);

        Assert.NotNull(loaded);
        Assert.True(loaded!.IsComplete);
        Assert.Equal("umxhq", loaded.ModelId);
        Assert.Equal(Source, loaded.SourcePath);
    }

    [Fact]
    public void TryLoad_ManifestPresentButStemFileMissing_IsMiss()
    {
        using var dir = new TempDirectory();
        var store = new StemStore(dir.Path);
        StemSet set = MakeSetWithRealFiles(store, dir);
        store.Save(set);

        // Delete one stem file on disk → an incomplete cache must be a miss (forces re-separation).
        File.Delete(set.StemPaths[StemKind.Bass]);

        Assert.Null(store.TryLoad(Source));
    }

    /// <summary>Builds a complete set whose four FLAC files actually exist inside the store folder.</summary>
    private static StemSet MakeSetWithRealFiles(StemStore store, TempDirectory dir)
    {
        string folder = store.FolderFor(Source);
        Directory.CreateDirectory(folder);
        var paths = new Dictionary<StemKind, string>();
        foreach (StemKind kind in StemSet.RequiredStems)
        {
            string p = Path.Combine(folder, kind.ToString().ToLowerInvariant() + ".flac");
            File.WriteAllText(p, "stub-flac");
            paths[kind] = p;
        }
        return new StemSet(Source, "umxhq", paths);
    }
}
