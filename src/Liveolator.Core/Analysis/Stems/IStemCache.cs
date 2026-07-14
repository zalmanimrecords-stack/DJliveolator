namespace Liveolator.Core.Analysis.Stems;

/// <summary>
/// Read-only lookup of the MANDATORY local stem cache (doc 32 §2.3) for the realtime load path: given a
/// track's source path, return its cached, complete <see cref="StemSet"/> or <c>null</c> on a miss. Core
/// seam so the realtime engine (Liveolator.Audio) reaches the cache without referencing the Media-layer
/// <c>StemStore</c> that implements it — and so the engine's load branch unit-tests with a fake. Pure
/// filesystem; never runs Python.
/// </summary>
public interface IStemCache
{
    /// <summary>The cached, complete stem set for <paramref name="sourcePath"/>, or <c>null</c> on a miss
    /// (no manifest, unreadable, incomplete, or any stem file missing on disk).</summary>
    StemSet? TryLoad(string sourcePath);
}
