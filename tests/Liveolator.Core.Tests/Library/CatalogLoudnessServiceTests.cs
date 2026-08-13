using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

/// <summary>
/// The loudness pass (<see cref="CatalogLoudnessService"/>): it must measure only the tracks that lack a
/// value, leave measured ones untouched, survive a file it cannot measure, persist, and report progress —
/// without ever throwing out of the loop (global standards #16/#26).
/// </summary>
public class CatalogLoudnessServiceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(string path, double? lufs = null, MediaAnalysisStatus status = MediaAnalysisStatus.Ok)
        => new(
            new ScannedFile(path, 10, T),
            new BpmResult(128.0, 0.9),
            new MusicalKey(0, KeyMode.Major, Camelot.Code(0, KeyMode.Major), 0.9),
            TimeSpan.FromMinutes(4), TrackCues.None, status, null,
            AnalyzerVersion: TrackAnalyzer.CurrentVersion,
            IntegratedLufs: lufs);

    private static MusicLibrary LibraryWith(params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(tracks);
        return library;
    }

    /// <summary>Returns whatever the map holds; a mapped null means "could not measure", missing throws.</summary>
    private sealed class MapLoudnessMeter : ILoudnessMeter
    {
        private readonly Dictionary<string, double?> _values;
        public int Calls { get; private set; }

        public MapLoudnessMeter(Dictionary<string, double?> values) => _values = values;

        public Task<double?> MeasureIntegratedLufsAsync(string path, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!_values.TryGetValue(path, out double? value))
                throw new InvalidOperationException($"unexpected measure of {path}");
            return Task.FromResult(value);
        }
    }

    [Fact]
    public async Task RunAsync_MeasuresOnlyUnmeasuredTracks_AndPersists()
    {
        // b already carries a value, so it must not be measured again — measuring is the expensive part.
        var meter = new MapLoudnessMeter(new() { ["a.wav"] = -8.4, ["c.wav"] = -11.2 });
        MusicLibrary library = LibraryWith(Track("a.wav"), Track("b.wav", lufs: -6.0), Track("c.wav"));
        var store = new RecordingCatalogStore();
        var progress = new List<LoudnessProgress>();

        var service = new CatalogLoudnessService(library, meter, store);
        LoudnessOutcome outcome = await service.RunAsync(new TestProgress<LoudnessProgress>(progress.Add));

        Assert.Equal(2, outcome.Considered);
        Assert.Equal(2, outcome.Measured);
        Assert.Equal(2, meter.Calls);
        Assert.Equal(-8.4, library.TryGet("a.wav")!.IntegratedLufs);
        Assert.Equal(-6.0, library.TryGet("b.wav")!.IntegratedLufs);
        Assert.Equal(-11.2, library.TryGet("c.wav")!.IntegratedLufs);
        Assert.NotEmpty(store.Saved);
        Assert.Equal(new LoudnessProgress(2, 2, 2), progress[^1]);
    }

    [Fact]
    public async Task RunAsync_KeepsGoing_WhenOneTrackCannotBeMeasured()
    {
        // An unreachable or silent file yields null; the rest of the pass must still complete.
        var meter = new MapLoudnessMeter(new() { ["gone.wav"] = null, ["ok.wav"] = -9.5 });
        MusicLibrary library = LibraryWith(Track("gone.wav"), Track("ok.wav"));

        LoudnessOutcome outcome = await new CatalogLoudnessService(library, meter).RunAsync();

        Assert.Equal(2, outcome.Considered);
        Assert.Equal(1, outcome.Measured);
        Assert.Null(library.TryGet("gone.wav")!.IntegratedLufs);
        Assert.Equal(-9.5, library.TryGet("ok.wav")!.IntegratedLufs);
    }

    [Fact]
    public async Task RunAsync_ReportsAnError_AndContinues_WhenTheMeterThrows()
    {
        var meter = new MapLoudnessMeter(new() { ["ok.wav"] = -9.0 });   // "boom.wav" is unmapped ⇒ throws
        MusicLibrary library = LibraryWith(Track("boom.wav"), Track("ok.wav"));
        var errors = new List<string>();

        LoudnessOutcome outcome = await new CatalogLoudnessService(
            library, meter, store: null, persistEvery: 25, onError: errors.Add).RunAsync();

        Assert.Equal(2, outcome.Considered);
        Assert.Equal(1, outcome.Measured);
        Assert.Contains(errors, e => e.Contains("boom.wav", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_SkipsFailedEntries_BecauseThereIsNothingToMeasure()
    {
        var meter = new MapLoudnessMeter(new());   // any call at all would throw
        MusicLibrary library = LibraryWith(Track("broken.wav", status: MediaAnalysisStatus.Failed));

        LoudnessOutcome outcome = await new CatalogLoudnessService(library, meter).RunAsync();

        Assert.Equal(0, outcome.Considered);
        Assert.Equal(0, meter.Calls);
    }

    [Fact]
    public async Task RunAsync_DoesNothing_WhenEveryTrackIsAlreadyMeasured()
    {
        var meter = new MapLoudnessMeter(new());
        MusicLibrary library = LibraryWith(Track("a.wav", lufs: -8.0));

        LoudnessOutcome outcome = await new CatalogLoudnessService(library, meter).RunAsync();

        Assert.Equal(new LoudnessOutcome(0, 0), outcome);
        Assert.Equal(0, meter.Calls);
    }
}
