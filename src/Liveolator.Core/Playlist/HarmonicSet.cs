using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist;

/// <summary>
/// One step in a generated set: the track plus why it follows the previous one (the harmonic
/// relationship and tempo change). Rationale is null for the seed (first) entry.
/// </summary>
public sealed record SetEntry(MusicTrack Track, TransitionRationale? Rationale);

/// <summary>
/// Why a track was chosen to follow its predecessor: the Camelot relationship and the BPM
/// change. <see cref="BpmDelta"/> is null when either track has no measured tempo.
/// </summary>
public sealed record TransitionRationale(string Relationship, double? BpmDelta);

/// <summary>
/// An ordered, harmonically-coherent set produced by <see cref="HarmonicSetBuilder"/>. The
/// first entry is the seed; each subsequent entry is harmonically compatible with the one
/// before it (Camelot rules) and respects the requested tempo trend. (Named distinctly from
/// the namespace so callers can reference it unqualified.)
/// </summary>
public sealed record HarmonicSet(IReadOnlyList<SetEntry> Entries)
{
    public int Count => Entries.Count;
}
