using System;
using System.Collections.Generic;
using System.Linq;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Maps a <see cref="StructuralCueResult"/> onto an 8-slot <see cref="TrackCueSet"/> using the
/// auto-cue bank convention (owner decision 2026-06-19): the UI shows 4 pads with an A/B bank
/// toggle, so the high-value performance cues go in <em>bank A</em> (slots 0–3) and the phrase
/// mix-points + outro go in <em>bank B</em> (slots 4–7):
/// <list type="bullet">
///   <item>A: 0 = Start, 1 = Drop, 2 = Breakdown, 3 = Build</item>
///   <item>B: 4–6 = Phrase mix points, 7 = Outro</item>
/// </list>
/// Pure and deterministic. Each speculative slot is gated independently by a confidence floor; the
/// always-safe Start/Outro pair is placed whenever present. Positions are snapped to the beat grid.
/// </summary>
public sealed class AutoCuePlacer
{
    private readonly double _minConfidence;

    /// <param name="minConfidence">Confidence floor for speculative cues (Drop/Breakdown/Build/Phrase);
    /// below this the slot is left empty. The Start/Outro pair bypasses the floor.</param>
    public AutoCuePlacer(double minConfidence = 0.5)
    {
        if (minConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minConfidence), "Must be in [0, 1].");
        _minConfidence = minConfidence;
    }

    private static readonly IReadOnlyDictionary<StructuralCueKind, int> FixedSlot =
        new Dictionary<StructuralCueKind, int>
        {
            [StructuralCueKind.TrackStart] = 0,
            [StructuralCueKind.Drop] = 1,
            [StructuralCueKind.Breakdown] = 2,
            [StructuralCueKind.BuildUp] = 3,
            [StructuralCueKind.OutroStart] = 7,
        };

    private static readonly int[] PhraseSlots = { 4, 5, 6 };

    /// <summary>The pad label + 0xRRGGBB color for each structural cue kind.</summary>
    private static readonly IReadOnlyDictionary<StructuralCueKind, (string Label, int Color)> Style =
        new Dictionary<StructuralCueKind, (string, int)>
        {
            [StructuralCueKind.TrackStart] = ("Start", 0xFFFFFF),
            [StructuralCueKind.Drop] = ("Drop", 0xFF3B30),
            [StructuralCueKind.Breakdown] = ("Breakdown", 0x0A84FF),
            [StructuralCueKind.BuildUp] = ("Build", 0xBF5AF2),
            [StructuralCueKind.Phrase] = ("Phrase", 0x32D74B),
            [StructuralCueKind.OutroStart] = ("Outro", 0xFF9F0A),
        };

    private static bool IsAlwaysSafe(StructuralCueKind kind) =>
        kind is StructuralCueKind.TrackStart or StructuralCueKind.OutroStart;

    /// <summary>
    /// Places the detected structure into a fresh cue set. Returns an empty set when there is nothing
    /// to place or the result is null.
    /// </summary>
    /// <param name="result">Detected structural cues.</param>
    /// <param name="bpm">Track tempo, used to beat-snap positions (must be positive).</param>
    /// <param name="sampleRate">Sample rate the cue offsets are measured against (must be positive).</param>
    /// <param name="slotCount">Number of hot-cue slots (default 8).</param>
    /// <param name="firstBeatSeconds">The beat-grid phase anchor (<see cref="Bpm.BpmResult.FirstBeatSeconds"/>):
    /// positions snap to <c>anchor + k·beat</c>, not to a grid starting at sample 0. Defaults to 0 (a
    /// sample-0 grid) so callers that have no phase anchor behave as before.</param>
    public TrackCueSet Place(
        StructuralCueResult? result, double bpm, int sampleRate,
        int slotCount = TrackCueSet.DefaultSlotCount, double firstBeatSeconds = 0.0)
    {
        if (bpm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(bpm), "BPM must be positive.");
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");

        var set = new TrackCueSet(sampleRate, slotCount);
        if (result is null || result.Cues.Count == 0)
            return set;

        double samplesPerBeat = sampleRate * 60.0 / bpm;
        double anchorSamples = Math.Max(0.0, firstBeatSeconds) * sampleRate;
        var placedSamples = new List<long>();

        // Bank A + the outro slot: one cue per fixed kind, gated unless always-safe.
        foreach ((StructuralCueKind kind, int slot) in FixedSlot)
        {
            if (slot >= slotCount)
                continue;
            StructuralCue cue = result.Cues.FirstOrDefault(c => c.Kind == kind);
            if (cue.Kind != kind)
                continue;
            if (!IsAlwaysSafe(kind) && cue.Confidence < _minConfidence)
                continue;

            long samples = SnapSamples(cue.PositionSeconds, samplesPerBeat, anchorSamples, sampleRate);
            (string label, int color) = Style[kind];
            set = set.SetHotCue(slot, samples, label, color, isAuto: true);
            placedSamples.Add(samples);
        }

        // Bank B phrase mix points: in track order, dropping any that collide with an already-placed
        // cue (within half a beat) so a phrase boundary doesn't duplicate the drop/breakdown pad.
        double collisionTolerance = samplesPerBeat / 2.0;
        (string phraseLabel, int phraseColor) = Style[StructuralCueKind.Phrase];
        int next = 0;
        foreach (StructuralCue phrase in result.Cues
                     .Where(c => c.Kind == StructuralCueKind.Phrase && c.Confidence >= _minConfidence)
                     .OrderBy(c => c.PositionSeconds))
        {
            if (next >= PhraseSlots.Length)
                break;
            int slot = PhraseSlots[next];
            if (slot >= slotCount)
                break;

            long samples = SnapSamples(phrase.PositionSeconds, samplesPerBeat, anchorSamples, sampleRate);
            if (placedSamples.Any(p => Math.Abs(p - samples) <= collisionTolerance))
                continue;

            set = set.SetHotCue(slot, samples, phraseLabel, phraseColor, isAuto: true);
            placedSamples.Add(samples);
            next++;
        }

        return set;
    }

    private static long SnapSamples(double seconds, double samplesPerBeat, double anchorSamples, int sampleRate)
    {
        double pos = Math.Max(0.0, seconds) * sampleRate;
        long beatIndex = (long)Math.Round((pos - anchorSamples) / samplesPerBeat);
        long snapped = (long)Math.Round(anchorSamples + beatIndex * samplesPerBeat);
        return snapped < 0 ? 0 : snapped;
    }
}
