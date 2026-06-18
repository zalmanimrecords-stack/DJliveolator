using System.Collections.Generic;
using Liveolator.Core.Studio;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// A bounded snapshot-based undo/redo stack for the STUDIO timeline. Each snapshot is a whole
/// <see cref="StudioProject"/> (an immutable record graph), so capturing/restoring reuses the
/// view-model's existing serialize (ToProject) / rebuild (LoadProject) seams instead of recording
/// per-edit deltas. Pure and UI-free so it unit-tests without Avalonia.
/// </summary>
/// <remarks>
/// Usage: call <see cref="Push"/> with the CURRENT project state immediately BEFORE a mutation.
/// <see cref="Undo"/> then takes that pushed state (and stashes the live state for redo);
/// <see cref="Redo"/> reverses it. The live/current project is owned by the caller (the view-model),
/// not by this class — the history only stores the snapshots either side of it.
/// </remarks>
public sealed class StudioEditHistory
{
    /// <summary>Default cap on how many undo steps are retained (oldest dropped past this).</summary>
    public const int DefaultDepth = 50;

    private readonly LinkedList<StudioProject> _undo = new();
    private readonly Stack<StudioProject> _redo = new();
    private readonly int _depth;

    public StudioEditHistory(int depth = DefaultDepth)
    {
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "History depth must be at least 1.");
        _depth = depth;
    }

    /// <summary>True when there is at least one state to undo to.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is at least one undone state to redo to.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The number of retained undo steps (for tests / diagnostics).</summary>
    public int UndoDepth => _undo.Count;

    /// <summary>The number of retained redo steps (for tests / diagnostics).</summary>
    public int RedoDepth => _redo.Count;

    /// <summary>
    /// Record <paramref name="current"/> as the state to return to. Call this with the project's
    /// state as it is just BEFORE applying a user mutation. A new edit invalidates the redo stack.
    /// No-op (and does not clear redo) when the state matches the most recent snapshot, so repeated
    /// identical pushes don't bloat or fragment the history.
    /// </summary>
    public void Push(StudioProject current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (_undo.Count > 0 && SnapshotsEqual(_undo.Last!.Value, current))
            return;

        _undo.AddLast(current);
        _redo.Clear();
        while (_undo.Count > _depth)
            _undo.RemoveFirst();
    }

    /// <summary>
    /// Pop the most recent snapshot to restore, stashing <paramref name="live"/> (the caller's
    /// current state) onto the redo stack. Returns null when there is nothing to undo.
    /// </summary>
    public StudioProject? Undo(StudioProject live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_undo.Count == 0)
            return null;

        StudioProject previous = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(live);
        return previous;
    }

    /// <summary>
    /// Pop the most recent undone snapshot to reapply, pushing <paramref name="live"/> (the caller's
    /// current state) back onto the undo stack. Returns null when there is nothing to redo.
    /// </summary>
    public StudioProject? Redo(StudioProject live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_redo.Count == 0)
            return null;

        StudioProject next = _redo.Pop();
        _undo.AddLast(live);
        while (_undo.Count > _depth)
            _undo.RemoveFirst();
        return next;
    }

    /// <summary>Drop all history (New / Open a project — the prior arrangement is no longer reachable).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    // Two snapshots are equal when their serializable content matches. StudioProject is a record but
    // its list members compare by reference, so compare the meaningful fields structurally to detect
    // genuine no-op pushes (e.g. a drag that ended where it started).
    private static bool SnapshotsEqual(StudioProject a, StudioProject b)
    {
        if (a.Name != b.Name || a.Bpm != b.Bpm)
            return false;
        if (a.Clips.Count != b.Clips.Count || a.Automation.Count != b.Automation.Count)
            return false;

        for (int i = 0; i < a.Clips.Count; i++)
            if (!a.Clips[i].Equals(b.Clips[i]))
                return false;

        for (int i = 0; i < a.Automation.Count; i++)
        {
            AutomationLane la = a.Automation[i];
            AutomationLane lb = b.Automation[i];
            if (la.Target != lb.Target || la.DeckSlot != lb.DeckSlot ||
                la.Keyframes.Count != lb.Keyframes.Count)
                return false;
            for (int k = 0; k < la.Keyframes.Count; k++)
                if (!la.Keyframes[k].Equals(lb.Keyframes[k]))
                    return false;
        }

        return TempoEqual(a.EffectiveTempo, b.EffectiveTempo);
    }

    private static bool TempoEqual(TempoCurve a, TempoCurve b)
    {
        if (a.Keyframes.Count != b.Keyframes.Count)
            return false;
        for (int i = 0; i < a.Keyframes.Count; i++)
            if (!a.Keyframes[i].Equals(b.Keyframes[i]))
                return false;
        return true;
    }
}
