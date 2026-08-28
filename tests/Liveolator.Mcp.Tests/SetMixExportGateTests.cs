using System.Text.Json;
using Liveolator.Audio.Render;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Set;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// The publish gate on <c>export_set_mix</c>. A refusal the owner cannot act on is just an obstacle, and a
/// gate that fires wrongly blocks every export — so each defect must be caught, each remedy named, and a
/// clean mix must sail through untouched. Every refusal here therefore has an acceptance case beside it: a
/// gate that always fired, or never fired, must fail this suite.
/// <para>At the 140 BPM used throughout, a bar is 1.714 s and the 8-bar blend floor is 13.71 s.</para>
/// <para>One thing this fixture cannot avoid: its "files" are zero bytes, so BASS decodes none of them and
/// every clip at warp factor 1.0 takes the renderer's managed MONO fallback. That is a real refusal (a mono
/// clip inside a stereo mix), so the tests that need a finished render pass <c>force</c> and assert on the
/// issue list instead of on <see cref="SetMixExport.Rendered"/>.</para>
/// </summary>
public sealed class SetMixExportGateTests : IDisposable
{
    private const double TempoBpm = 140.0;
    private const string SetName = "Gate Set";

    /// <summary>One bar of the set tempo — the unit every blend length below is written in.</summary>
    private const double Bar = 4 * 60.0 / TempoBpm;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"liveolator-gate-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_Refuses_WhenAClipRunsAtItsNativeTempo()
    {
        // The worst defect a "beat-matched" mix can ship with: the clip drifts for its whole length.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", bpm: 145.0) },
            Clip("a.mp3", start: 0, seconds: 60, warped: false, sourceBpm: 145.0, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        Assert.Null(export.AudioPath);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("drifts", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("145", issue.Problem, StringComparison.Ordinal);
        Assert.Contains("excludeLowGridConfidence", issue.Remedy, StringComparison.Ordinal);
        Assert.True(issue.Blocking);
    }

    [Fact]
    public async Task Export_Refuses_WhenAClipWasNeverMeasured()
    {
        // Unity gain on an UNMEASURED track means this clip steps in level against its neighbours.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", lufs: null) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 1.0));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("level-matched", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("measure_catalog_loudness", issue.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_SaysNothing_ForATrackMeasuredExactlyAtTarget()
    {
        // ffmpeg's ebur128 prints one decimal place, so a track landing exactly on the -9.0 target is a
        // routine measurement — and its gain is then exactly 1.0. Telling the owner to go and measure it is
        // advice that changes nothing, on a set that was refused for it.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", lufs: -9.0) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 1.0));

        SetMixExport export = await Export(session, force: true);

        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("unity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_Refuses_WhenABlendIsUnderTheEightBarFloor()
    {
        // A 2 s overlap at 140 BPM is barely more than a bar: it reads as a cut, not a mix.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 58, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("reads as a cut", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlapBars", issue.Remedy, StringComparison.Ordinal);
        Assert.True(issue.Blocking);
    }

    [Fact]
    public async Task Export_Refuses_WhenMostJoinsSitExactlyOnTheEightBarFloor()
    {
        // 8 bars is the arranger's distress signal, not a pass: psytrance wants 16-32. The old comparison was
        // strictly-under, so a blend clamped all the way down landed exactly on the floor and said nothing.
        double aEnd = 60.0;
        double bStart = aEnd - (8 * Bar);
        double bEnd = bStart + 60.0;
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3"), Track("c.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: bStart, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("c.mp3", start: bEnd - (8 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue[] floor = export.Issues
            .Where(i => i.Problem.Contains("floor", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, floor.Length);
        Assert.All(floor, i => Assert.True(i.Blocking));
        Assert.All(floor, i => Assert.DoesNotContain("reads as a cut", i.Problem, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_ReportsWithoutBlocking_WhenOneJoinOfThreeSitsOnTheFloor()
    {
        // Owner decision: one clamped join is worth saying, not worth refusing — only a set that is MOSTLY at
        // the floor is an arrangement that failed.
        double aEnd = 60.0;
        double bStart = aEnd - 20.0;
        double bEnd = bStart + 60.0;
        double cStart = bEnd - 20.0;
        double cEnd = cStart + 60.0;
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3"), Track("c.mp3"), Track("d.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: bStart, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("c.mp3", start: cStart, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("d.mp3", start: cEnd - (8 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("floor", StringComparison.OrdinalIgnoreCase));
        Assert.False(issue.Blocking);
    }

    [Fact]
    public async Task Export_Accepts_ANineBarBlend()
    {
        // The boundary from the other side: one bar over the floor is a mix, and must not be flagged at all.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (9 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("floor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("cut", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_DoesNotReportACut_ForAnUnwarpedClipFasterThanTheSetTempo()
    {
        // An unwarped clip's blend is bars of ITS OWN tempo, so measuring it against the set's bar length
        // reported a legitimate 8-bar blend (12.8 s at 150 BPM) as under the 13.71 s floor — a lie that fires
        // on exactly the low-confidence tracks already forced down to 8 bars.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", bpm: 150.0), Track("b.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: false, sourceBpm: 150.0, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (8 * (4 * 60.0 / 150.0)), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("reads as a cut", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(export.Issues, i => i.Problem.Contains("floor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_Refuses_WhenAJoinOpensOverBeatlessMaterial()
    {
        // The measured 2026-08-13 defect: the blend opens where the incoming record has no drums at all, and
        // every existing check passed — both clips warped, both gained, a full 16-bar overlap.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3", kicks: Kicks(TempoBpm, gaps: (0.0, 200.0))) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (16 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("beatless", StringComparison.OrdinalIgnoreCase));
        Assert.True(issue.Blocking);

        // The audit collapses both sides into one verdict per join, so the line is only actionable if it names
        // both records.
        Assert.Contains(Path.Combine(_directory, "a.mp3"), issue.Where, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(_directory, "b.mp3"), issue.Where, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Accepts_AJoinThatOpensOverKicks()
    {
        // The same geometry with drums running through the entry must produce nothing.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (16 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("beatless", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("EITHER deck", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_Refuses_WhenADropLandsInsideTheOverlap()
    {
        // The incoming record's drop hits while the outgoing one is still playing over it: the train wreck.
        var structure = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(10.0, SongSectionLabel.Drop),
            },
            "test");
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3", structure: structure) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (16 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("drop", StringComparison.OrdinalIgnoreCase));
        Assert.True(issue.Blocking);
    }

    [Fact]
    public async Task Export_Refuses_WhenNeitherDeckHasAKickForTwoBars()
    {
        // The hole the owner heard: not one record withdrawing, but both at once. Each side alone still clears
        // its own coverage floor here (14 of 16 bars), so only the JOINT run finds it.
        double bStart = 60.0 - (16 * Bar);
        DjSetSession session = await SessionWithCatalogAsync(
            new[]
            {
                // Bars 4-5 of the outgoing window, which opens at bStart in its own source seconds.
                Track("a.mp3", kicks: Kicks(TempoBpm, gaps: (bStart + (4 * Bar), bStart + (6 * Bar)))),
                Track("b.mp3", kicks: Kicks(TempoBpm, gaps: (4 * Bar, 6 * Bar))),
            },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: bStart, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("EITHER deck", StringComparison.Ordinal));
        Assert.True(issue.Blocking);
        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("beatless", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_Refuses_WhenAClampedGainLeavesAClipWellBelowTheTarget()
    {
        // The +6 dB boost limit returns its boundary with no signal to anyone, and the gate only ever looked
        // for unity — so a record that no gain can lift to the set level shipped silently.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", lufs: -20.0) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 2.0));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("LUFS", StringComparison.Ordinal));
        Assert.Contains("-20", issue.Problem, StringComparison.Ordinal);
        Assert.True(issue.Blocking);
    }

    [Fact]
    public async Task Export_SaysNothing_WhenTheClampCostsUnderADecibel()
    {
        // Measured: a -15.3 LUFS track against the -9.0 target is left 0.28 dB short by the clamp, not the 8 dB
        // first claimed. A threshold read off that anecdote would fire on every slightly-quiet master.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", lufs: -15.3) },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 2.0));

        SetMixExport export = await Export(session, force: true);

        Assert.DoesNotContain(export.Issues, i => i.Problem.Contains("LUFS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_SaysUnverifiable_WhenAClipsTrackIsGoneFromTheCatalog()
    {
        // Silence about a clip nothing is known about is the lie this audit exists to stop — but not knowing
        // is not proof of a defect, so it reports without blocking.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 60.0 - (16 * Bar), seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("cannot be verified", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("b.mp3", issue.Where, StringComparison.OrdinalIgnoreCase);
        Assert.False(issue.Blocking);
    }

    [Fact]
    public async Task Export_ReportsTheRenderedHoles_FromTheRenderResult()
    {
        // Whole-file loudness provably cannot find these (a 95%-silent mix once measured -10.30 LUFS), and a
        // 6 s hole is 20% of this mix — well under the mostly-silent throw. Without this the only way to find
        // it is to listen to the whole render.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3") },
            new HoleDecoder(loudSeconds: 12.0, silentSeconds: 6.0),
            NullLoudnessMeter.Instance,
            Clip("a.mp3", start: 0, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        Assert.True(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("dBFS", StringComparison.Ordinal));
        Assert.Contains("0:12", issue.Where, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_Refuses_WhenAClipRenderedInMono()
    {
        // BASS cannot open this fixture's zero-byte files, so the clip takes the renderer's managed mono
        // fallback — the path that once shipped eleven minutes of a 68-minute export with no stereo image,
        // announced only in a log.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3") },
            Clip("a.mp3", start: 0, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(
            export.Issues, i => i.Problem.Contains("MONO", StringComparison.Ordinal));
        Assert.True(issue.Blocking);

        // The audio is there to listen to, but no publish package was written for it.
        Assert.NotNull(export.AudioPath);
        Assert.Null(export.TracklistPath);
    }

    [Fact]
    public async Task Export_ReportsEveryDefect_NotJustTheFirst()
    {
        // The owner should learn everything that needs fixing in one pass, not one problem per attempt.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3", bpm: 145.0, lufs: null) },
            Clip("a.mp3", start: 0, seconds: 60, warped: false, sourceBpm: 145.0, gain: 1.0));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        Assert.Equal(2, export.Issues.Count);
    }

    [Fact]
    public async Task Export_RendersAnyway_WhenForced_AndStillReportsTheIssues()
    {
        // Once the owner has listened and decided, the tool must not stand in the way — but it also must
        // not quietly pretend the mix was clean.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 5, warped: false, sourceBpm: 145.0, gain: 1.0));

        SetMixExport export = await Export(session, force: true);

        Assert.True(export.Rendered);
        Assert.NotEmpty(export.Issues);
        Assert.NotNull(export.AudioPath);
        Assert.True(File.Exists(export.AudioPath));
    }

    [Fact]
    public async Task Export_RendersAndWritesTheTracklist_WhenTheCatalogGateIsClean()
    {
        // A 20 s blend clears the 13.71 s floor, both clips are warped to the set tempo, level-matched, and
        // catalogued with drums running through the entry — so the catalog-derived gate finds nothing at all.
        // Forced because this fixture's render always takes the mono fallback (see the class remark).
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3") },
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 40, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.9));

        SetMixExport export = await Export(session, force: true);

        Assert.All(export.Issues, i => Assert.Contains("MONO", i.Problem, StringComparison.Ordinal));
        Assert.True(export.Rendered);
        Assert.True(File.Exists(export.AudioPath));
        Assert.True(File.Exists(export.TracklistPath));
        Assert.True(File.Exists(export.ChaptersPath));

        // YouTube only builds chapters when the first one is 00:00.
        string[] chapters = await File.ReadAllLinesAsync(export.ChaptersPath!);
        Assert.Equal(2, chapters.Length);
        Assert.StartsWith("0:00 ", chapters[0], StringComparison.Ordinal);
        Assert.StartsWith("0:40 ", chapters[1], StringComparison.Ordinal);

        // The machine-readable tracklist carries the exact positions the chapters were rounded from.
        var entries = JsonSerializer.Deserialize<List<MixTrackEntry>>(
            await File.ReadAllTextAsync(export.TracklistPath!),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(entries);
        Assert.Equal(new[] { 1, 2 }, entries!.Select(e => e.Index));
        Assert.Equal(0.0, entries[0].StartSeconds);
        Assert.Equal(40.0, entries[1].StartSeconds);
    }

    [Fact]
    public async Task Export_ReportsTheLoudnessItCouldNotMeasure_AsNullRatherThanTheTarget()
    {
        // The meter here measures nothing. Reporting the target as though it had been measured would be
        // the one dishonesty that matters in a publish report.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 5, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session, force: true);

        Assert.True(export.Rendered);
        Assert.Null(export.IntegratedLufs);
        Assert.Equal(-1.0, export.CeilingDbTp);
    }

    [Fact]
    public async Task Export_Fails_WhenASourceFileIsUnreachable_EvenWhenForced()
    {
        // This catalog lives partly on a network share. A mix with silent stretches that nothing reports is
        // the worst possible outcome of a long unattended render, so force must not reach past this.
        DjSetSession session = await SessionWithSetAsync(
            createFiles: false,
            Clip("gone.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => Export(session, force: true));

        Assert.Contains("not reachable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gone.mp3", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Fails_WhenASourceDecodedToNothing_EvenThoughEveryGateIsClean()
    {
        // The 703 MB of digital silence, reproduced: a set that passes every publish check — both clips
        // warped to the set tempo, both level-matched, a 20 s blend — whose sources decode to nothing.
        // Rendering it and reporting success is the worst outcome of a long unattended export.
        DjSetSession session = await SessionWithCatalogAsync(
            new[] { Track("a.mp3"), Track("b.mp3") },
            new SilentDecoder(),
            NullLoudnessMeter.Instance,
            Clip("a.mp3", start: 0, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 10, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.9));

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => Export(session, force: true));

        Assert.Contains("decoded to nothing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a.mp3", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was published", error.Message, StringComparison.OrdinalIgnoreCase);

        // No half publish package: the tracklist would read as a finished, publishable mix.
        Assert.False(File.Exists(Path.Combine(_directory, "out", $"{SetName}-tracklist.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "out", $"{SetName}-youtube.txt")));
    }

    [Fact]
    public async Task Export_Fails_WhenTheWarpedDecodePathReturnsEmpty_EvenWhenForced()
    {
        // Warp is no longer gated on phase confidence, so every clip takes the native time-stretch path:
        // one BASS problem silences the whole mix rather than a few clips. This clip needs a real stretch
        // (128 against 140), and its source is a zero-byte file, so the stretch decode comes back empty
        // whether or not BASS is available on the machine running the test.
        DjSetSession session = await SessionWithSetAsync(
            Clip("warped.mp3", start: 0, seconds: 20, warped: true, sourceBpm: 128.0, gain: 0.8));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Export(session, force: true));

        Assert.Contains("decoded to nothing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warped.mp3", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Fails_WhenTheMeasuredLoudnessIsNotFinite()
    {
        // -infinity LUFS is ffmpeg's way of saying the file carries no signal at all.
        DjSetSession session = await SessionWithSetAsync(
            createFiles: true, new ToneDecoder(), new FixedLoudnessMeter(double.NegativeInfinity),
            Clip("a.mp3", start: 0, seconds: 5, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Export(session, force: true));

        Assert.Contains("no signal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Fails_WhenMostOfTheMixIsSilence_ThoughEverySourceDecoded()
    {
        // Whole-file loudness cannot catch this: a mix that is mostly silence still measures a healthy
        // number, because the part that does sound carries the average. Here every source decodes, but
        // only 2 s of a 30 s clip has audio in it.
        DjSetSession session = await SessionWithSetAsync(
            createFiles: true, new ToneDecoder(seconds: 2.0), new FixedLoudnessMeter(-10.3),
            Clip("a.mp3", start: 0, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Export(session, force: true));

        Assert.Contains("silence", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a continuous mix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Rejects_AnUnknownSetName()
    {
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 5, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        await Assert.ThrowsAsync<ArgumentException>(() => DjSetTools.ExportSetMix(
            session, "no such set", Path.Combine(_directory, "out")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private Task<SetMixExport> Export(DjSetSession session, bool force = false)
        => DjSetTools.ExportSetMix(session, SetName, Path.Combine(_directory, "out"), force);

    private StudioClip Clip(
        string file, double start, double seconds, bool warped, double sourceBpm, double gain)
        => new(
            DeckSlot: 0,
            TrackPath: Path.Combine(_directory, file),
            TimelineStartSeconds: start,
            SourceIn: TimeSpan.Zero,
            SourceOut: TimeSpan.FromSeconds(seconds),
            SourceBpm: sourceBpm,
            WarpEnabled: warped,
            Gain: gain);

    /// <summary>
    /// A catalogued track the gate can actually judge: measured, cleanly gridded, and with a kick on every
    /// beat for ten minutes. Override one field per test so a single defect is under test at a time.
    /// </summary>
    private MusicTrack Track(
        string file,
        double bpm = TempoBpm,
        double? lufs = -11.0,
        IReadOnlyList<double>? kicks = null,
        SongStructure? structure = null)
        => new(
            new ScannedFile(Path.Combine(_directory, file), 100, DateTime.UtcNow),
            new BpmResult(bpm, 0.9)
            {
                BeatsPerBar = 4,
                DownbeatConfidence = 0.8,
                GridCoherence = 0.9,
                TempoStabilityBpmDelta = 0.1,
                KickOnsetsSeconds = kicks ?? Kicks(bpm),
            },
            new MusicalKey(0, KeyMode.Minor, "8A", 0.8),
            TimeSpan.FromMinutes(10),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            TrackMetadata.Empty with { Title = Path.GetFileNameWithoutExtension(file), Artist = "Tester" },
            MusicMediaKind.Track,
            TrackAnalyzer.CurrentVersion,
            Structure: structure,
            IntegratedLufs: lufs);

    /// <summary>A kick on every beat for ten minutes, minus any <paramref name="gaps"/> (in seconds).</summary>
    private static double[] Kicks(double bpm, params (double From, double To)[] gaps)
    {
        double beat = 60.0 / bpm;
        var kicks = new List<double>();
        for (int i = 0; i * beat < 600.0; i++)
        {
            double t = i * beat;
            if (gaps.Any(g => t >= g.From && t < g.To))
                continue;
            kicks.Add(t);
        }

        return kicks.ToArray();
    }

    private Task<DjSetSession> SessionWithSetAsync(params StudioClip[] clips)
        => SessionWithSetAsync(createFiles: true, clips);

    private Task<DjSetSession> SessionWithSetAsync(bool createFiles, params StudioClip[] clips)
        => SessionWithSetAsync(createFiles, new ToneDecoder(), NullLoudnessMeter.Instance, clips);

    private Task<DjSetSession> SessionWithSetAsync(
        bool createFiles, IAudioDecoder decoder, ILoudnessMeter meter, params StudioClip[] clips)
        => SessionAsync(createFiles, decoder, meter, Array.Empty<MusicTrack>(), clips);

    private Task<DjSetSession> SessionWithCatalogAsync(MusicTrack[] catalog, params StudioClip[] clips)
        => SessionAsync(createFiles: true, new ToneDecoder(), NullLoudnessMeter.Instance, catalog, clips);

    private Task<DjSetSession> SessionWithCatalogAsync(
        MusicTrack[] catalog, IAudioDecoder decoder, ILoudnessMeter meter, params StudioClip[] clips)
        => SessionAsync(createFiles: true, decoder, meter, catalog, clips);

    private async Task<DjSetSession> SessionAsync(
        bool createFiles,
        IAudioDecoder decoder,
        ILoudnessMeter meter,
        MusicTrack[] catalog,
        params StudioClip[] clips)
    {
        Directory.CreateDirectory(_directory);
        if (createFiles)
        {
            foreach (StudioClip clip in clips)
                await File.WriteAllBytesAsync(clip.TrackPath, Array.Empty<byte>());
        }

        // Deck slots must alternate for two clips to overlap on separate lanes, as the arranger lays them out.
        StudioClip[] laid = clips
            .Select((c, i) => c with { DeckSlot = i % 2 })
            .ToArray();

        var catalogStore = new JsonCatalogStore(_directory);
        if (catalog.Length > 0)
            await catalogStore.SaveMusicAsync(catalog);

        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        var library = new LibrarySession(
            new EmptyEnumerator(),
            decoder,
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            catalogStore,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLoudnessMeter.Instance,
            NullLogger<LibrarySession>.Instance);

        var projectStore = new JsonStudioProjectStore(_directory);
        await projectStore.SaveAsync(
            new StudioProject(SetName, TempoBpm, laid, Array.Empty<AutomationLane>()));

        return new DjSetSession(
            library,
            projectStore,
            new OfflineMixRenderer(decoder),
            meter,
            NullLogger<DjSetSession>.Instance);
    }

    private sealed class EmptyEnumerator : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(
            IReadOnlyList<string> rootDirectories, IReadOnlySet<string> extensions)
            => Array.Empty<ScannedFile>();
    }

    /// <summary>
    /// Decodes a constant level, so a rendered mix actually contains audio. It has to: the export now
    /// refuses a silent mix, and a fixture that decoded nothing would have every gate test passing on a
    /// file of digital silence — which is the defect these tests exist to keep out.
    /// </summary>
    private sealed class ToneDecoder : IAudioDecoder
    {
        private const float Level = 0.5f;
        private const int BlockFloats = 4096;

        private readonly double _seconds;

        /// <param name="seconds">How much audio the file yields. The default covers every clip in this
        /// fixture; a shorter one leaves the rest of the clip as silence, which is its own defect.</param>
        internal ToneDecoder(double seconds = 90.0) => _seconds = seconds;

        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var block = new float[BlockFloats];
            Array.Fill(block, Level);

            long remaining = (long)(_seconds * targetSampleRate);
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int take = (int)Math.Min(BlockFloats, remaining);
                yield return block.AsMemory(0, take);
                remaining -= take;
            }
        }
    }

    /// <summary>
    /// Loud, then digitally silent, then loud again — a hole INSIDE a clip, which is what the 2026-08-13
    /// export shipped and what no whole-file measurement can see.
    /// </summary>
    private sealed class HoleDecoder : IAudioDecoder
    {
        private const int BlockFloats = 4096;

        private readonly double _loudSeconds;
        private readonly double _silentSeconds;

        internal HoleDecoder(double loudSeconds, double silentSeconds)
        {
            _loudSeconds = loudSeconds;
            _silentSeconds = silentSeconds;
        }

        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach ((double seconds, float level) in new[]
                     {
                         (_loudSeconds, 0.5f), (_silentSeconds, 0.0f), (_loudSeconds, 0.5f),
                     })
            {
                var block = new float[BlockFloats];
                Array.Fill(block, level);
                long remaining = (long)(seconds * targetSampleRate);
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int take = (int)Math.Min(BlockFloats, remaining);
                    yield return block.AsMemory(0, take);
                    remaining -= take;
                }
            }
        }
    }

    /// <summary>Decodes nothing at all — the failure BASS produced when it was not initialised.</summary>
    private sealed class SilentDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>Reports one fixed measurement, so the export's reaction to it can be tested.</summary>
    private sealed class FixedLoudnessMeter : ILoudnessMeter
    {
        private readonly double? _lufs;

        internal FixedLoudnessMeter(double? lufs) => _lufs = lufs;

        public Task<double?> MeasureIntegratedLufsAsync(
            string path, CancellationToken cancellationToken = default) => Task.FromResult(_lufs);
    }
}
