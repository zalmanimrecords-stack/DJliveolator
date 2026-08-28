namespace Liveolator.Core.Studio.Set;

/// <summary>
/// How a DJ set is built. The harmonic ordering itself is governed separately by
/// <see cref="Liveolator.Core.Playlist.HarmonicSetOptions"/>; this covers tempo, transitions and gating.
/// </summary>
/// <param name="ProjectName">Name of the produced <see cref="StudioProject"/> (also its store key).</param>
/// <param name="OverlapBars">Requested crossfade length in bars. Rounded down to a multiple of
/// <see cref="PhraseBars"/>/2 and clamped to [<see cref="MinOverlapBars"/>, <see cref="MaxOverlapBars"/>];
/// a transition may still shorten it when the runway is tight.</param>
/// <param name="MaxWarpPercent">How far a track may be time-stretched to reach the set tempo, as a
/// percentage. 6 suits 4/4 electronic material; drop to 3 for vocal-forward or live-drummed music, where
/// stretch artefacts and groove change show up roughly twice as early.</param>
/// <param name="ExcludeLowGridConfidence">When true, a track whose beat grid is not trustworthy is kept
/// out of the set entirely instead of being mixed short and unwarped.</param>
/// <param name="TempoBpm">The tempo every clip is warped to. Null lets the arranger take the median of the
/// tracks it chose, which is a sensible default and nothing more — the set tempo is a musical decision the
/// DJ owns, and a median cannot express "this is a 140 room". Note the median is computed from the selected
/// tracks, so a pool weighted toward one tempo pins it there and no amount of reordering moves it. Setting
/// this does not suspend <see cref="MaxWarpPercent"/>: a track that cannot reach the chosen tempo within the
/// ceiling is still rejected and named.</param>
/// <param name="StartDeckSlot">Deck lane (0 or 1) the first clip lands on; clips then alternate.</param>
/// <param name="TargetLufs">Integrated loudness every clip is gained toward, so unequal masters sit level
/// through each crossfade. −9 suits dance music, whose masters already sit around −8 to −6: a target near
/// their natural level leaves the master limiter barely working. Streaming platforms only ever attenuate on
/// normalization, so aiming lower would sound identical after their pass and merely spend headroom.</param>
public sealed record SetBuildOptions(
    string ProjectName = "DJ Set",
    int OverlapBars = 16,
    double MaxWarpPercent = 6.0,
    bool ExcludeLowGridConfidence = false,
    double? TempoBpm = null,
    int StartDeckSlot = 0,
    double TargetLufs = -9.0)
{
    /// <summary>The project meter. Two decks in 4/4 is the DJ model and the only tested render path.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>A phrase — the unit dance music is actually written in, and the grid every mix point is
    /// quantized to so two warped clips stay phrase-aligned across a whole crossfade.</summary>
    public const int PhraseBars = 16;

    /// <summary>Below this a blend reads as a mistake rather than a mix, so it is the floor: a transition
    /// that cannot fit it rejects the track instead of degrading further.</summary>
    public const int MinOverlapBars = 8;

    /// <summary>Above this two full arrangements fight each other, and any residual grid error has a
    /// minute to accumulate into audible flam.</summary>
    public const int MaxOverlapBars = 32;

    /// <summary>Overlap granularity — half a phrase, so every crossfade is phrase-integer.</summary>
    public const int OverlapStepBars = PhraseBars / 2;

    /// <summary>A track failing the grid-confidence gate is never blended longer than this.</summary>
    public const int LowConfidenceOverlapBars = MinOverlapBars;

    /// <summary>The requested overlap normalized into the supported range and granularity.</summary>
    public int NormalizedOverlapBars => ClampOverlapBars(OverlapBars);

    /// <summary>Rounds <paramref name="bars"/> down to the overlap granularity and into the legal range.</summary>
    public static int ClampOverlapBars(int bars)
    {
        int stepped = bars / OverlapStepBars * OverlapStepBars;
        return Math.Clamp(stepped, MinOverlapBars, MaxOverlapBars);
    }

    /// <summary>Seconds one bar lasts at <paramref name="bpm"/>.</summary>
    public static double BarSeconds(double bpm)
        => bpm > 0.0 ? BeatsPerBar * 60.0 / bpm : 0.0;

    /// <summary>Validates the request, throwing for values that cannot produce a set.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
            throw new ArgumentException("Set name cannot be empty.", nameof(ProjectName));
        if (OverlapBars < MinOverlapBars)
            throw new ArgumentOutOfRangeException(nameof(OverlapBars), OverlapBars, $"Overlap must be at least {MinOverlapBars} bars.");
        if (OverlapBars > MaxOverlapBars)
            throw new ArgumentOutOfRangeException(nameof(OverlapBars), OverlapBars, $"Overlap cannot exceed {MaxOverlapBars} bars.");
        if (MaxWarpPercent <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(MaxWarpPercent), MaxWarpPercent, "Warp limit must be positive.");
        if (TempoBpm is <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(TempoBpm), TempoBpm, "Set tempo must be positive.");
        if (StartDeckSlot is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(StartDeckSlot), StartDeckSlot, "Start deck slot must be 0 or 1 (clips alternate between the two).");
    }
}
