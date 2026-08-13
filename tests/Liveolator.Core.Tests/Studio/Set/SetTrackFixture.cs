using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// Builds analyzed tracks for the set-building tests. At 128 BPM a bar is 1.875 s and a 16-bar phrase is
/// exactly 30 s, which keeps the expected positions in the tests readable.
/// </summary>
internal static class SetTrackFixture
{
    private static readonly DateTime Stamp = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Grid signals that clear the phase-sync gate (tight kick fit, constant tempo).</summary>
    private const double TrustedCoherence = 0.9;
    private const double TrustedStability = 0.1;

    /// <summary>A five-minute analyzed track with a trustworthy grid and no structure analysis.</summary>
    internal static MusicTrack Track(
        string path,
        string camelot = "8A",
        double bpm = 128.0,
        double durationSeconds = 300.0,
        SongStructure? structure = null,
        IReadOnlyList<double>? kicks = null,
        double? gridCoherence = TrustedCoherence,
        double? tempoStability = TrustedStability,
        double downbeatSeconds = 0.0,
        double? integratedLufs = null)
        => new(
            new ScannedFile(path, 1_000, Stamp),
            new BpmResult(bpm, 0.9)
            {
                DownbeatSeconds = downbeatSeconds,
                BeatsPerBar = 4,
                DownbeatConfidence = 0.8,
                GridCoherence = gridCoherence,
                TempoStabilityBpmDelta = tempoStability,
                KickOnsetsSeconds = kicks ?? Array.Empty<double>(),
            },
            new MusicalKey(0, KeyMode.Minor, camelot, 0.9),
            TimeSpan.FromSeconds(durationSeconds),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            Structure: structure,
            IntegratedLufs: integratedLufs);

    /// <summary>A track whose beat grid fails the phase-sync gate (loose kick fit, drifting tempo).</summary>
    internal static MusicTrack UntrustedGrid(string path, string camelot = "8A", double bpm = 128.0)
        => Track(path, camelot, bpm, gridCoherence: 0.2, tempoStability: 3.0);

    /// <summary>A track analyzed before grid confidence existed — quality unknown, phase sync preserved.</summary>
    internal static MusicTrack UnanalyzedGrid(string path, string camelot = "8A", double bpm = 128.0)
        => Track(path, camelot, bpm, gridCoherence: null, tempoStability: null);

    /// <summary>
    /// A rock-steady record whose kick reads soft: the tempo is constant so it can be warped, but the grid
    /// fit is too loose to align phase against. The case that separates a tempo downgrade from a phase one.
    /// </summary>
    internal static MusicTrack SmearedKick(string path, string camelot = "8A", double bpm = 128.0)
        => Track(path, camelot, bpm, gridCoherence: 0.2, tempoStability: TrustedStability);

    /// <summary>
    /// The usual shape of a dance record, on bar lines at 128 BPM: intro, build, drop, breakdown, second
    /// drop, outro. Trusted by the planner (six sections, labelled, aligned to the grid).
    /// </summary>
    internal static SongStructure StandardStructure()
        => new(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(60.0, SongSectionLabel.BuildUp),
                new SongSection(90.0, SongSectionLabel.Drop),
                new SongSection(150.0, SongSectionLabel.Breakdown),
                new SongSection(180.0, SongSectionLabel.Drop),
                new SongSection(240.0, SongSectionLabel.Outro),
            },
            "test");

    internal static SongStructure Structure(params SongSection[] sections) => new(sections, "test");
}
