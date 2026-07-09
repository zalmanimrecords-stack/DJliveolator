using System.Collections.Generic;

namespace Liveolator.Core.Analysis.Stems;

/// <summary>
/// The result of separating one track into stems (doc 32 §2.3): the source file, the model that
/// produced them, and the path to each of the four stem files (FLAC sidecars). Pure data — produced
/// offline / at import time, cached locally, never touched on the realtime path. The stem paths are
/// always local (never a network drive) by the time a deck loads them.
/// </summary>
/// <param name="SourcePath">The original track the stems were separated from.</param>
/// <param name="ModelId">The separator model id, e.g. <c>"umxhq"</c> (Open-Unmix) or <c>"htdemucs"</c>.</param>
/// <param name="StemPaths">Absolute path to each stem file, keyed by <see cref="StemKind"/>.</param>
public sealed record StemSet(
    string SourcePath,
    string ModelId,
    IReadOnlyDictionary<StemKind, string> StemPaths)
{
    /// <summary>The four stem kinds that must be present for a complete set.</summary>
    public static readonly IReadOnlyList<StemKind> RequiredStems =
        new[] { StemKind.Drums, StemKind.Bass, StemKind.Vocals, StemKind.Other };

    /// <summary>
    /// The position of <paramref name="kind"/> in <see cref="RequiredStems"/>, or -1 if not required. The
    /// realtime stem submix creates its inner decoders (and the deck tracks per-stem mute) in this order,
    /// so this is the single source of truth mapping a stem kind to its decoder/state index.
    /// </summary>
    public static int IndexOf(StemKind kind)
    {
        for (int i = 0; i < RequiredStems.Count; i++)
            if (RequiredStems[i] == kind)
                return i;
        return -1;
    }

    /// <summary>True when a path is present for all four required stems.</summary>
    public bool IsComplete
    {
        get
        {
            foreach (StemKind kind in RequiredStems)
                if (!StemPaths.ContainsKey(kind))
                    return false;
            return true;
        }
    }
}
