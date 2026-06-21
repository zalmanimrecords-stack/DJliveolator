using System.Collections.Generic;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// One track parsed from another DJ app's library, in a source-agnostic form. <see cref="SourcePath"/>
/// is the file path exactly as the source app wrote it — it may not resolve on this machine, so the
/// planner remaps it against the local catalog/filesystem before use. Every analysis field is optional
/// because sources vary in what they store; the planner fills only what is present.
/// </summary>
/// <param name="SourcePath">Absolute file path as written by the source app (the join key, pre-remap).</param>
/// <param name="Title">Tag title; null falls back to the filename downstream.</param>
/// <param name="Artist">Tag artist.</param>
/// <param name="Album">Tag album.</param>
/// <param name="Genre">Tag genre.</param>
/// <param name="Year">Release year.</param>
/// <param name="Comment">Tag comment.</param>
/// <param name="DurationSeconds">Track length in seconds (used for path-remap disambiguation).</param>
/// <param name="Bpm">Source tempo in BPM; null when the source had none.</param>
/// <param name="FirstBeatSeconds">Beat-grid anchor in seconds from track start (the first downbeat).</param>
/// <param name="Key">Raw key string from the source (Camelot "8A", classical "Am", or Open Key "1m").</param>
/// <param name="Cues">Parsed cue points (hot cues + any memory cue); null/empty when none.</param>
public sealed record ImportedTrack(
    string SourcePath,
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? Genre = null,
    int? Year = null,
    string? Comment = null,
    double? DurationSeconds = null,
    double? Bpm = null,
    double? FirstBeatSeconds = null,
    string? Key = null,
    IReadOnlyList<ImportedCue>? Cues = null);
