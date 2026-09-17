using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using System.Linq;
using Xunit;

namespace Liveolator.Core.Tests.Library.Import;

/// <summary>
/// The pull is one-way, server to local, and fills gaps only. Two rules carry the whole design and both
/// are asserted here: the DJ's own work is never overwritten (a hand correction, a local tempo, every
/// library field), and nothing the server sends is ever stamped as if this machine had analyzed it.
/// </summary>
public class ServerCatalogPullTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ServerScan = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A bare catalogued row; every test shapes it further with <c>with</c> so the 20-odd
    /// positional fields never have to be spelled out (and never silently mis-ordered).</summary>
    private static MusicTrack Track(string path)
        => new(
            new ScannedFile(path, 4096, T),
            Bpm: null,
            Key: null,
            Duration: null,
            TrackCues.None,
            MediaAnalysisStatus.PartiallyAnalyzed,
            Error: null);

    private static MusicalKey Key(string camelot, double confidence)
        => new(0, KeyMode.Major, camelot, confidence);

    private static ServerCatalogPullPlan Plan(
        MusicTrack server, MusicTrack local, IReadOnlySet<string>? inUse = null)
        => ServerCatalogPull.Plan(new[] { server }, new[] { local }, null, null, inUse);

    // ---------- what the server is for ----------

    [Fact]
    public void Plan_FillsAnEmptyGrid_FromTheServer()
    {
        MusicTrack local = Track("/m/a.mp3");
        MusicTrack server = Track("/m/a.mp3") with
        {
            Bpm = new BpmResult(145.0, 0.9, FirstBeatSeconds: 0.31) { DownbeatSeconds = 1.97 },
            AnalyzerVersion = 9,
            LastAnalyzedUtc = ServerScan,
        };

        ServerCatalogPullPlan plan = Plan(server, local);
        MusicTrack merged = Assert.Single(plan.TracksToUpsert);

        // The whole BpmResult, not just the number — the anchor and downbeat are what make a track
        // phase-sync eligible, and they are the reason the server exists at all.
        Assert.Equal(145.0, merged.Bpm!.Bpm, 6);
        Assert.Equal(1.97, merged.Bpm.DownbeatSeconds, 6);
        Assert.Equal(0.31, merged.Bpm.FirstBeatSeconds, 6);
        Assert.Equal(MediaAnalysisStatus.Ok, merged.Status);
        Assert.Equal(1, plan.TracksGainingAnalysis);
    }

    /// <summary>
    /// Never stamped as now: this machine did not analyze the file. A fresh date here would make "last
    /// scanned" a lie and hide a stale server behind it.
    /// </summary>
    [Fact]
    public void Plan_CreditsTheServersScanDate_NotThisMachines()
    {
        MusicTrack local = Track("/m/a.mp3");
        MusicTrack server = Track("/m/a.mp3") with
        {
            Bpm = new BpmResult(145.0, 0.9),
            AnalyzerVersion = 9,
            LastAnalyzedUtc = ServerScan,
        };

        MusicTrack merged = Assert.Single(Plan(server, local).TracksToUpsert);

        Assert.Equal(ServerScan, merged.LastAnalyzedUtc);
        Assert.Equal(9, merged.AnalyzerVersion);
    }

    [Fact]
    public void Plan_TakesTheServerKey_WhenTheLocalOneIsTooWeakToTrust()
    {
        MusicTrack local = Track("/m/a.mp3") with { Key = Key("8A", 0.1) };
        MusicTrack server = Track("/m/a.mp3") with { Key = Key("3B", 0.9) };

        MusicTrack merged = Assert.Single(Plan(server, local).TracksToUpsert);

        Assert.Equal("3B", merged.Key!.Camelot);
    }

    // ---------- what the DJ keeps ----------

    /// <summary>
    /// Local wins, always and silently. The disagreement is surfaced for later review through the same
    /// conflict badge the online BPM cross-check already ships — it is never resolved during the pull.
    /// </summary>
    [Fact]
    public void Plan_KeepsTheLocalTempo_AndFlagsTheDisagreement()
    {
        MusicTrack local = Track("/m/a.mp3") with { Bpm = new BpmResult(140.0, 0.9) };
        MusicTrack server = Track("/m/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = Plan(server, local);
        MusicTrack merged = Assert.Single(plan.TracksToUpsert);

        Assert.Equal(140.0, merged.Bpm!.Bpm, 6);
        Assert.Equal(BpmProvenance.Conflicted, merged.BpmProvenance);
        Assert.Equal(145.0, merged.OnlineBpm);
        Assert.Equal(ServerCatalogPull.DisagreementSource, merged.OnlineBpmSource);

        ServerCatalogDisagreement flagged = Assert.Single(plan.Disagreements);
        Assert.Equal(140.0, flagged.LocalBpm, 6);
        Assert.Equal(145.0, flagged.ServerBpm, 6);
    }

    /// <summary>
    /// The veto. <see cref="MusicTrack.AnalysisIsManual"/> is not merely data — it is the flag that
    /// exempts a row from re-analysis, so writing here would discard the correction AND re-arm the
    /// automatic pass that overwrites it again, leaving no trace it ever existed.
    /// </summary>
    [Fact]
    public void Plan_NeverTouchesAHandCorrectedTrack()
    {
        MusicTrack local = Track("/m/a.mp3") with { AnalysisIsManual = true };
        MusicTrack server = Track("/m/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = Plan(server, local);

        Assert.Empty(plan.TracksToUpsert);
        Assert.Equal(1, plan.HandCorrectedUntouched);
    }

    /// <summary>Rewriting a loaded track's grid changes how the mixer behaves under the DJ's hands
    /// mid-mix, so a track on a deck is deferred rather than written.</summary>
    [Fact]
    public void Plan_DefersATrackThatIsLoadedOnADeck()
    {
        MusicTrack local = Track("/m/a.mp3");
        MusicTrack server = Track("/m/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = Plan(
            server, local, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/m/a.mp3" });

        Assert.Empty(plan.TracksToUpsert);
        Assert.Equal(1, plan.SkippedInUse);
    }

    /// <summary>
    /// Library fields are the DJ's, not the analyzer's, and the merge preserves them by construction
    /// rather than by listing them. This pins that, because the failure mode is silent data loss.
    /// </summary>
    [Fact]
    public void Plan_PreservesEveryLocalLibraryField()
    {
        DateTime added = new(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        DateTime played = new(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        DateTime lookedUp = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);

        MusicTrack local = Track("/m/a.mp3") with
        {
            Rating = 5,
            PlayCount = 7,
            DateAdded = added,
            LastPlayed = played,
            OnlineLookupUtc = lookedUp,
            Kind = MusicMediaKind.Sample,
            Metadata = new TrackMetadata(
                "My Title", "My Artist", null, null, "psytrance", null, null, Comment: "mine",
                null, null, null, null),
        };
        MusicTrack server = Track("/m/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        MusicTrack merged = Assert.Single(Plan(server, local).TracksToUpsert);

        Assert.Equal(5, merged.Rating);
        Assert.Equal(7, merged.PlayCount);
        Assert.Equal(added, merged.DateAdded);
        Assert.Equal(played, merged.LastPlayed);
        Assert.Equal(lookedUp, merged.OnlineLookupUtc);
        Assert.Equal(MusicMediaKind.Sample, merged.Kind);
        Assert.Equal("My Title", merged.Metadata!.Title);
        Assert.Equal("mine", merged.Metadata.Comment);
    }

    // ---------- scope ----------

    /// <summary>v1 enriches rows this catalog already holds and never adds new ones, so a server row
    /// with no local counterpart is counted and skipped rather than imported.</summary>
    [Fact]
    public void Plan_SkipsARowThisCatalogHasNeverSeen()
    {
        MusicTrack local = Track("/m/a.mp3");
        MusicTrack server = Track("/m/somewhere-else.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = Plan(server, local);

        Assert.Empty(plan.TracksToUpsert);
        Assert.Equal(1, plan.NotFoundLocally);
    }

    /// <summary>The server and this machine mount the same library at different roots, so paths are
    /// rebased before they are matched.</summary>
    [Fact]
    public void Plan_RebasesServerPathsOntoTheLocalRoot()
    {
        MusicTrack local = Track(@"\\simonsrv\Storage\music\a.mp3");
        MusicTrack server = Track("/srv/music/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = ServerCatalogPull.Plan(
            new[] { server }, new[] { local }, "/srv/music", @"\\simonsrv\Storage\music");

        MusicTrack merged = Assert.Single(plan.TracksToUpsert);
        Assert.Equal(@"\\simonsrv\Storage\music\a.mp3", merged.File.Path);
        Assert.Equal(145.0, merged.Bpm!.Bpm, 6);
    }

    /// <summary>A pull that would change nothing writes nothing — re-running it is free and idempotent.</summary>
    [Fact]
    public void Plan_WritesNothing_WhenTheServerAddsNothing()
    {
        MusicTrack local = Track("/m/a.mp3") with
        {
            Bpm = new BpmResult(145.0, 0.9),
            Key = Key("8A", 0.9),
            Status = MediaAnalysisStatus.Ok,
            BpmProvenance = BpmProvenance.LocalDetected,
        };
        MusicTrack server = local;

        ServerCatalogPullPlan plan = Plan(server, local);

        Assert.Empty(plan.TracksToUpsert);
        Assert.Empty(plan.Disagreements);
    }

    // ---------- applying the plan ----------

    [Fact]
    public void Apply_ReplacesTheEnrichedRow_AndCarriesEveryOtherRowThrough()
    {
        MusicTrack enriched = Track("/m/a.mp3");
        MusicTrack untouched = Track("/m/b.mp3") with { Rating = 4 };
        MusicTrack server = Track("/m/a.mp3") with { Bpm = new BpmResult(145.0, 0.9) };

        ServerCatalogPullPlan plan = ServerCatalogPull.Plan(
            new[] { server }, new[] { enriched, untouched }, null, null);

        IReadOnlyList<MusicTrack> after = ServerCatalogPull.Apply(new[] { enriched, untouched }, plan);

        Assert.Equal(2, after.Count); // an upsert replaces, it never duplicates the row
        Assert.Equal(145.0, after.Single(t => t.File.Path == "/m/a.mp3").Bpm!.Bpm, 6);
        Assert.Equal(4, after.Single(t => t.File.Path == "/m/b.mp3").Rating);
    }

    [Fact]
    public void Apply_WithAnEmptyPlan_ReturnsTheCatalogUnchanged()
    {
        MusicTrack local = Track("/m/a.mp3") with { Rating = 3 };

        IReadOnlyList<MusicTrack> after =
            ServerCatalogPull.Apply(new[] { local }, ServerCatalogPullPlan.Empty);

        Assert.Equal(3, Assert.Single(after).Rating);
    }

    [Fact]
    public void Describe_LeadsWithTheGain_AndEndsWithTheProtection()
    {
        var plan = new ServerCatalogPullPlan(
            Array.Empty<MusicTrack>(), TracksGainingAnalysis: 12, TracksNowPhaseSyncReady: 9,
            NotFoundLocally: 0, HandCorrectedUntouched: 3, SkippedInUse: 0,
            Array.Empty<ServerCatalogDisagreement>());

        string line = plan.Describe();

        Assert.StartsWith("Added analysis to 12 track(s)", line);
        Assert.Contains("9 can now phase-sync", line);
        Assert.Contains("3 hand-corrected track(s) untouched", line);
    }
}
