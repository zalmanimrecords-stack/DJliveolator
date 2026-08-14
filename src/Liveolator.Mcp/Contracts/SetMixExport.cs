namespace Liveolator.Mcp.Contracts;

/// <summary>One reason a set is not ready to publish, with the remedy that clears it.</summary>
/// <param name="Where">Which transition or clip the problem is in, in human terms.</param>
/// <param name="Problem">What is wrong.</param>
/// <param name="Remedy">The concrete next action — the whole point of refusing rather than rendering.</param>
public sealed record MixGateIssue(string Where, string Problem, string Remedy);

/// <summary>One track as it appears in the finished mix.</summary>
/// <param name="Index">1-based position in the mix.</param>
/// <param name="Artist">Tag artist, or null when untagged.</param>
/// <param name="Title">Display title.</param>
/// <param name="StartSeconds">Where it starts in the mix.</param>
/// <param name="Timestamp">The same position as <c>mm:ss</c> (or <c>h:mm:ss</c> past the hour), the form a
/// YouTube description needs for chapters.</param>
public sealed record MixTrackEntry(int Index, string? Artist, string Title, double StartSeconds, string Timestamp);

/// <summary>
/// Result of exporting a set as one continuous mix. When <see cref="Rendered"/> is false the export refused
/// on quality grounds and <see cref="Issues"/> says why — the verdict is the deliverable in that case, not
/// the audio.
/// <para>This record is only ever returned for a mix that contains audio. A render that came back silent —
/// a source that decoded to nothing, a non-finite measured loudness, or mostly-silent output — throws
/// instead, force or not, because "rendered a mix with silent stretches" is not a result anyone can use.
/// So <see cref="Rendered"/> true means the audio is really there, and <see cref="IntegratedLufs"/> is
/// either a finite measurement or null.</para>
/// </summary>
/// <param name="Rendered">False when the publish gate refused (pass force to override).</param>
/// <param name="AudioPath">The rendered WAV, or null when nothing was rendered.</param>
/// <param name="TracklistPath">Machine-readable tracklist JSON, or null.</param>
/// <param name="ChaptersPath">YouTube description/chapter text, or null.</param>
/// <param name="DurationSeconds">Length of the mix.</param>
/// <param name="IntegratedLufs">Measured integrated loudness OF THE RENDERED FILE, or null when it could
/// not be measured. Measured, not assumed — the mix is the thing being published. Note what this number
/// cannot tell you: whole-file loudness of a 95%-silent mix once read a perfectly healthy -10.3 LUFS,
/// because the few clips that did sound carried the average. Silence is caught by the render report, not
/// here.</param>
/// <param name="CeilingDbTp">The true-peak ceiling the master limiter was configured with. A configured
/// bound, not a measurement of the output.</param>
/// <param name="Issues">Everything the gate found, whether or not the render went ahead.</param>
/// <param name="Tracks">The mix's tracklist in play order.</param>
public sealed record SetMixExport(
    bool Rendered,
    string? AudioPath,
    string? TracklistPath,
    string? ChaptersPath,
    double DurationSeconds,
    double? IntegratedLufs,
    double CeilingDbTp,
    IReadOnlyList<MixGateIssue> Issues,
    IReadOnlyList<MixTrackEntry> Tracks);
