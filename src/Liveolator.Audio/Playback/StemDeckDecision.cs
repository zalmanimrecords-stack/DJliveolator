using System.IO;
using Liveolator.Core.Analysis.Stems;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Pure decision for whether a deck <c>Load</c> should open a 4-stem submix deck instead of the normal
/// single-file deck (doc 32 §Phase 2b). Stems are attempted only when ALL hold: the default-off gate is
/// on, a COMPLETE cached <see cref="StemSet"/> exists for the track, and every stem path is local (never a
/// network drive — stems must never be decoded from S: on the realtime load path). Pure + static so the
/// load branch is unit-testable without BASS or a filesystem.
/// </summary>
internal static class StemDeckDecision
{
    /// <summary>
    /// True when the load should use <paramref name="set"/> as a stem deck. <paramref name="set"/> is the
    /// cache lookup result (null = miss). Reasons for false are returned in <paramref name="reason"/> for
    /// logging.
    /// </summary>
    public static bool ShouldUseStems(bool gateEnabled, StemSet? set, out string reason)
    {
        if (!gateEnabled)
        {
            reason = "stems gate off";
            return false;
        }
        if (set is null)
        {
            reason = "no cached stems";
            return false;
        }
        if (!set.IsComplete)
        {
            reason = "incomplete stem set";
            return false;
        }
        foreach (string path in set.StemPaths.Values)
        {
            if (!IsLocalPath(path))
            {
                reason = "stem path is not local";
                return false;
            }
        }
        reason = "complete local stem set";
        return true;
    }

    /// <summary>
    /// A path is local for stem playback when it is fully qualified and NOT a UNC network share
    /// (<c>\\server\share</c> or <c>//server/share</c>). The mandatory local cache (<c>StemStore</c>)
    /// already roots stems under %LOCALAPPDATA%; this guards against a manifest that points elsewhere.
    /// </summary>
    public static bool IsLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        return !(path.StartsWith(@"\\") || path.StartsWith("//"));
    }
}
