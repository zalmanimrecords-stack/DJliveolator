using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio;

/// <summary>
/// Derives a sensible default <see cref="StudioTransition"/> between two adjacent tracks so a
/// freshly auto-planned set already has editable transitions. Pure and IO-free.
/// </summary>
/// <remarks>
/// Blend lengths are expressed in <em>beats</em> on purpose: a musical phrase is a fixed number of
/// beats regardless of tempo, so a beat-denominated length stays phrase-aligned when the playback
/// tempo changes (whereas a seconds-denominated length would not).
/// </remarks>
public static class TransitionDefaults
{
    /// <summary>A full eight-bar phrase. Used when phrase cues let us anchor outro→intro safely.</summary>
    public const double PhraseBlendBeats = 32;

    /// <summary>A shorter four-bar overlap. The fallback when no phrase cues exist, so an unanchored
    /// (blind) tail overlap is less likely to clash with the tracks' structure.</summary>
    public const double BlindOverlapBeats = 16;

    /// <summary>
    /// Chooses the default handover from <paramref name="from"/> into <paramref name="to"/>:
    /// a hard <see cref="StudioTransition.Cut"/> when either track lacks a tempo (can't beat-match),
    /// otherwise a constant-power bass-swap blend anchored to phrase cues when both tracks expose
    /// them, or a shorter blind tail overlap when they don't.
    /// </summary>
    public static StudioTransition For(MusicTrack from, MusicTrack to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        // Without tempo on both sides we cannot beat-match; a hard cut is the only honest handover.
        if (from.Bpm is null || to.Bpm is null)
            return StudioTransition.Cut;

        bool hasPhraseCues = from.Cues.OutroStart.HasValue && to.Cues.IntroEnd.HasValue;
        TransitionAnchor anchor = hasPhraseCues ? TransitionAnchor.OutroToIntro : TransitionAnchor.TailOverlap;
        double lengthBeats = hasPhraseCues ? PhraseBlendBeats : BlindOverlapBeats;

        return new StudioTransition(TransitionKind.BassSwap, lengthBeats, CrossfaderCurve.Smooth, anchor);
    }
}
