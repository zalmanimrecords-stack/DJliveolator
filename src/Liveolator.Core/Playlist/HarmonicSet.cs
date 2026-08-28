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
/// Which of the chain's two rules kept a candidate out. The distinction is the caller's next move: a key
/// ring that does not reach means widen the pool, a trend lockout means drop the trend or reseed low.
/// </summary>
public enum HarmonicVeto
{
    /// <summary>Not Camelot-compatible with the track the chain had reached.</summary>
    NoCompatibleKey,

    /// <summary>Compatible in key, but the tempo step to it was out of bounds for the requested
    /// <see cref="BpmTrend"/> — in the wrong direction, or beyond the trend's tolerance.</summary>
    BlockedByTrend,

    /// <summary>Never asked: the requested length was reached first. Not a veto, and not a rejection.</summary>
    NotTried,
}

/// <summary>
/// A candidate that never made the chain, and which rule kept it out. Reported so "the set came back
/// short" is never the only signal the caller has — with an empty leftover list the only reading left is
/// "the library is thin", which is the wrong next call in both of the common cases.
/// </summary>
public sealed record UnpickedCandidate(MusicTrack Track, HarmonicVeto Veto);

/// <summary>
/// An ordered, harmonically-coherent set produced by <see cref="HarmonicSetBuilder"/>. The
/// first entry is the seed; each subsequent entry is harmonically compatible with the one
/// before it (Camelot rules) and respects the requested tempo trend. (Named distinctly from
/// the namespace so callers can reference it unqualified.)
/// </summary>
/// <param name="Unpicked">Every candidate the chain left behind, with the rule that kept it out.</param>
public sealed record HarmonicSet(
    IReadOnlyList<SetEntry> Entries,
    IReadOnlyList<UnpickedCandidate> Unpicked)
{
    public int Count => Entries.Count;
}
