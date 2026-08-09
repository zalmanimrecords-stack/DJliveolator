using Liveolator.Audio;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using ManagedBass;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// Proves <c>render_set_preview</c> actually produces audio from this process. Every clip in a built set
/// is warped, so the render runs entirely through BASS_FX — which means the MCP server needs the native
/// BASS libraries beside it, exactly as the app does (src/Bass.Native.targets ships them to both).
/// <para>CI has no native BASS, so this test skips there — the same shape as
/// <c>BassFxNativeSmokeTests</c>. On a real machine it fails loudly if the natives ever stop reaching the
/// server's output, which would otherwise only show up as an empty render at runtime.</para>
/// </summary>
public sealed class DjSetPreviewRenderTests : IDisposable
{
    private const int SampleRate = 44_100;
    private const double TrackSeconds = 150.0;
    private const double Bpm = 128.0;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"liveolator-djset-render-{Guid.NewGuid():N}");

    [Fact]
    public async Task RenderSetPreview_WritesAudibleTransitions()
    {
        if (!NativeBassAvailable())
            return;   // no native BASS in this environment (CI) — nothing to prove here.

        DjSetSession session = await CreateSessionAsync();
        await DjSetTools.BuildDjSet(session, seedPath: Path.Combine(_directory, "a.wav"), length: 2, name: "Render Test");

        string previews = Path.Combine(_directory, "previews");
        SetPreviewResult result = await DjSetTools.RenderSetPreview(session, "Render Test", previews, SampleRate);

        SetPreviewClip clip = Assert.Single(result.Clips);
        Assert.True(File.Exists(clip.OutputPath), $"no preview written at {clip.OutputPath}");
        Assert.True(clip.DurationSeconds > 0);

        // A WAV of pure silence is what a missing native, an unreachable source, or a broken slice all
        // produce, so the guard has to be on the samples rather than on the file existing.
        Assert.True(PeakAmplitude(clip.OutputPath) > 0.01, "the rendered transition is silent");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static bool NativeBassAvailable()
    {
        try
        {
            return Bass.Init(0) || Bass.LastError == Errors.Already;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    private async Task<DjSetSession> CreateSessionAsync()
    {
        Directory.CreateDirectory(_directory);
        MusicTrack[] tracks = { WriteTrack("a.wav", 440.0), WriteTrack("b.wav", 660.0) };

        var store = new JsonCatalogStore(_directory);
        await store.SaveMusicAsync(tracks);
        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        var library = new LibrarySession(
            new FileSystemFileEnumerator(),
            new CompositeAudioDecoder(new FfmpegOptions(null)),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            store,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLogger<LibrarySession>.Instance);

        return new DjSetSession(
            library,
            new JsonStudioProjectStore(_directory),
            new OfflineMixRenderer(new CompositeAudioDecoder(new FfmpegOptions(null))),
            NullLogger<DjSetSession>.Instance);
    }

    // A steady tone is enough: the point is whether samples come out the other end, not what they sound like.
    private MusicTrack WriteTrack(string file, double frequency)
    {
        string path = Path.Combine(_directory, file);
        var samples = new float[(int)(TrackSeconds * SampleRate)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f * (float)Math.Sin(2.0 * Math.PI * frequency * i / SampleRate);
        WavWriter.WriteMono(path, samples, SampleRate);

        return new MusicTrack(
            new ScannedFile(path, samples.Length * 2, DateTime.UtcNow),
            new BpmResult(Bpm, 0.9) { BeatsPerBar = 4, GridCoherence = 0.9, TempoStabilityBpmDelta = 0.1 },
            new MusicalKey(0, KeyMode.Minor, "8A", 0.8),
            TimeSpan.FromSeconds(TrackSeconds),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            TrackMetadata.Empty with { Title = Path.GetFileNameWithoutExtension(file) },
            MusicMediaKind.Track,
            TrackAnalyzer.CurrentVersion);
    }

    private static double PeakAmplitude(string wavPath)
    {
        using var reader = new BinaryReader(File.OpenRead(wavPath));
        reader.BaseStream.Seek(44, SeekOrigin.Begin);   // past the canonical 44-byte header

        double peak = 0.0;
        while (reader.BaseStream.Position + 1 < reader.BaseStream.Length)
            peak = Math.Max(peak, Math.Abs(reader.ReadInt16() / (double)short.MaxValue));
        return peak;
    }
}
