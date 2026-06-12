using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;

namespace Liveolator.Core.Studio;

/// <summary>
/// Plans a named <see cref="StudioSet"/>: orders tracks with the existing
/// <see cref="HarmonicSetBuilder"/> (Camelot + tempo trend) and assigns each entry a default
/// transition via <see cref="TransitionDefaults"/>, so an auto-built set is immediately playable
/// and editable. Pure and IO-free — it composes Core services and adds no new ordering logic.
/// </summary>
public sealed class StudioSetPlanner
{
    private readonly HarmonicSetBuilder _builder;

    public StudioSetPlanner(HarmonicSetBuilder? builder = null)
        => _builder = builder ?? new HarmonicSetBuilder();

    /// <summary>
    /// Builds a set named <paramref name="name"/> starting at <paramref name="seed"/>, drawing from
    /// <paramref name="candidates"/>. The first entry has no incoming transition; every later entry
    /// carries the default transition from its predecessor.
    /// </summary>
    public StudioSet BuildFrom(string name, MusicTrack seed, IEnumerable<MusicTrack> candidates, HarmonicSetOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        HarmonicSet harmonic = _builder.Build(seed, candidates, options);

        var entries = new List<StudioEntry>(harmonic.Count);
        MusicTrack? previous = null;
        foreach (SetEntry entry in harmonic.Entries)
        {
            StudioTransition? transition = previous is null
                ? null
                : TransitionDefaults.For(previous, entry.Track);
            entries.Add(new StudioEntry(entry.Track.File.Path, TransitionIn: transition));
            previous = entry.Track;
        }

        return new StudioSet(name, entries);
    }
}
