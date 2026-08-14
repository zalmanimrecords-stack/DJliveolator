using System.Text.Json;
using Liveolator.Audio.Render;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
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
/// clean mix must sail through untouched.
/// <para>At the 140 BPM used throughout, a bar is 1.714 s and the 8-bar blend floor is 13.71 s.</para>
/// </summary>
public sealed class SetMixExportGateTests : IDisposable
{
    private const double TempoBpm = 140.0;
    private const string SetName = "Gate Set";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"liveolator-gate-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_Refuses_WhenAClipRunsAtItsNativeTempo()
    {
        // The worst defect a "beat-matched" mix can ship with: the clip drifts for its whole length.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 60, warped: false, sourceBpm: 145.0, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        Assert.Null(export.AudioPath);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("drifts", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("145", issue.Problem, StringComparison.Ordinal);
        Assert.Contains("excludeLowGridConfidence", issue.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_Refuses_WhenAClipIsLeftAtUnityGain()
    {
        // Unity gain means no loudness was measured, so this clip steps in level against its neighbours.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 1.0));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("level-matched", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("measure_catalog_loudness", issue.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_Refuses_WhenABlendIsUnderTheEightBarFloor()
    {
        // A 2 s overlap at 140 BPM is barely more than a bar: it reads as a cut, not a mix.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 58, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8));

        SetMixExport export = await Export(session);

        Assert.False(export.Rendered);
        MixGateIssue issue = Assert.Single(export.Issues);
        Assert.Contains("blends for only", issue.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlapBars", issue.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_ReportsEveryDefect_NotJustTheFirst()
    {
        // The owner should learn everything that needs fixing in one pass, not one problem per attempt.
        DjSetSession session = await SessionWithSetAsync(
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
    public async Task Export_RendersAndWritesTheTracklist_WhenTheMixIsClean()
    {
        // A 20 s blend clears the 13.71 s floor, both clips are warped to the set tempo and gained.
        DjSetSession session = await SessionWithSetAsync(
            Clip("a.mp3", start: 0, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 40, seconds: 60, warped: true, sourceBpm: TempoBpm, gain: 0.9));

        SetMixExport export = await Export(session);

        Assert.Empty(export.Issues);
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

        SetMixExport export = await Export(session);

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
        DjSetSession session = await SessionWithSetAsync(
            createFiles: true, new SilentDecoder(), NullLoudnessMeter.Instance,
            Clip("a.mp3", start: 0, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.8),
            Clip("b.mp3", start: 10, seconds: 30, warped: true, sourceBpm: TempoBpm, gain: 0.9));

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => Export(session));

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

    private Task<DjSetSession> SessionWithSetAsync(params StudioClip[] clips)
        => SessionWithSetAsync(createFiles: true, clips);

    private Task<DjSetSession> SessionWithSetAsync(bool createFiles, params StudioClip[] clips)
        => SessionWithSetAsync(createFiles, new ToneDecoder(), NullLoudnessMeter.Instance, clips);

    private async Task<DjSetSession> SessionWithSetAsync(
        bool createFiles, IAudioDecoder decoder, ILoudnessMeter meter, params StudioClip[] clips)
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
