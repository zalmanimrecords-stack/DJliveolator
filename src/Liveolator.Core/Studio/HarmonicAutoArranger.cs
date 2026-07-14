using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;

namespace Liveolator.Core.Studio;

/// <summary>
/// Auto-arranges a set of analyzed tracks into a STUDIO project, dj.studio "Harmonize"-style:
/// orders them harmonically (Camelot-compatible, tempo-smooth) via <see cref="HarmonicSetBuilder"/>,
/// then lays the clips out back-to-back on alternating deck lanes with a fixed crossfade overlap.
/// Pure and IO-free — operates on the in-memory track list, so it unit-tests without hardware.
/// </summary>
public sealed class HarmonicAutoArranger
{
    private readonly HarmonicSetBuilder _builder = new();

    /// <summary>
    /// Produces a <see cref="StudioProject"/> from <paramref name="tracks"/>. The ordering follows
    /// <see cref="HarmonicSetBuilder"/> seeded by the first eligible (analyzed + keyed) track in
    /// input order; clips are placed on alternating deck slots and overlapped by
    /// <see cref="AutoArrangeOptions.OverlapSeconds"/> with matching fade-out/fade-in. Returns an
    /// empty project when no track can seed a harmonic set (no analyzed, keyed track present).
    /// </summary>
    public StudioProject Arrange(
        IReadOnlyList<MusicTrack> tracks,
        HarmonicSetOptions harmonicOptions,
        AutoArrangeOptions arrangeOptions)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(harmonicOptions);
        ArgumentNullException.ThrowIfNull(arrangeOptions);
        harmonicOptions.Validate();
        arrangeOptions.Validate();

        MusicTrack? seed = FirstEligibleSeed(tracks);
        if (seed is null)
            return StudioProject.Empty(arrangeOptions.ProjectName);

        HarmonicSet ordered = _builder.Build(seed, tracks, harmonicOptions);
        if (ordered.Count == 0)
            return StudioProject.Empty(arrangeOptions.ProjectName);

        IReadOnlyList<StudioClip> clips = LayOut(ordered, arrangeOptions);
        double bpm = ProjectBpm(ordered);

        return new StudioProject(
            arrangeOptions.ProjectName,
            bpm,
            clips,
            Array.Empty<AutomationLane>());
    }

    /// <summary>
    /// The first track that can seed a harmonic set: analyzed (not failed) and keyed. The builder
    /// rejects an unkeyed seed, so we skip such tracks here to keep arrange total over any input.
    /// </summary>
    private static MusicTrack? FirstEligibleSeed(IReadOnlyList<MusicTrack> tracks)
    {
        foreach (MusicTrack track in tracks)
        {
            if (track.Status != MediaAnalysisStatus.Failed && track.Key is not null)
                return track;
        }

        return null;
    }

    private static IReadOnlyList<StudioClip> LayOut(HarmonicSet ordered, AutoArrangeOptions options)
    {
        int count = ordered.Count;
        var clips = new List<StudioClip>(count);
        double cursorStart = 0.0;

        for (int i = 0; i < count; i++)
        {
            MusicTrack track = ordered.Entries[i].Track;
            int deckSlot = (options.StartDeckSlot + i) % 2;

            TimeSpan? sourceOut = track.Duration;  // null ⇒ open-ended clip (unknown length)
            // Crossfade only where there is a real neighbour: fade in from the previous clip,
            // fade out into the next. End clips keep their natural edge.
            double fadeIn = i > 0 ? options.OverlapSeconds : 0.0;
            double fadeOut = i < count - 1 ? options.OverlapSeconds : 0.0;

            clips.Add(new StudioClip(
                DeckSlot: deckSlot,
                TrackPath: track.File.Path,
                TimelineStartSeconds: cursorStart,
                SourceIn: TimeSpan.Zero,
                SourceOut: sourceOut,
                FadeOutSeconds: fadeOut,
                FadeInSeconds: fadeIn));

            // Advance the cursor for the next clip: it begins `overlap` before this clip ends so the
            // two crossfade. With an unknown length we cannot compute an end, so the next clip starts
            // at the same position — keeping starts monotonic non-decreasing without guessing a length.
            if (track.Duration is { } duration)
            {
                double nextStart = cursorStart + duration.TotalSeconds - options.OverlapSeconds;
                cursorStart = Math.Max(cursorStart, nextStart);
            }
        }

        return clips;
    }

    /// <summary>Project tempo = the first ordered (seed) track's BPM, or the project default when unknown.</summary>
    private static double ProjectBpm(HarmonicSet ordered)
        => ordered.Entries[0].Track.Bpm?.Bpm ?? StudioProject.DefaultBpm;
}
