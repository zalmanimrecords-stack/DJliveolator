using System;
using System.Collections.Generic;
using Liveolator.App.Features.Studio;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Unit tests for the pure snapshot undo/redo stack: bounded depth, no-op de-duplication, the
/// undo/redo round-trip, and that a fresh push invalidates redo. No Avalonia — the history only moves
/// immutable <see cref="StudioProject"/> snapshots.
/// </summary>
public sealed class StudioEditHistoryTests
{
    private static StudioProject Project(string name, params (int deck, double start)[] clips)
    {
        var list = new List<StudioClip>();
        foreach ((int deck, double start) in clips)
            list.Add(new StudioClip(deck, $"/m/{start}.wav", start, TimeSpan.Zero, null));
        return new StudioProject(name, StudioProject.DefaultBpm, list, Array.Empty<AutomationLane>());
    }

    [Fact]
    public void Empty_HasNothingToUndoOrRedo()
    {
        var history = new StudioEditHistory();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_ThenUndo_RestoresThePushedState_AndEnablesRedo()
    {
        var history = new StudioEditHistory();
        StudioProject before = Project("p", (0, 0));   // one clip
        StudioProject after = Project("p", (0, 0), (1, 8)); // two clips (the "current" live state)

        history.Push(before);
        Assert.True(history.CanUndo);

        StudioProject? restored = history.Undo(after);
        Assert.NotNull(restored);
        Assert.Single(restored!.Clips); // back to the one-clip state
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesTheUndoneState()
    {
        var history = new StudioEditHistory();
        StudioProject before = Project("p", (0, 0));
        StudioProject after = Project("p", (0, 0), (1, 8));

        history.Push(before);
        history.Undo(after);             // now live == before, redo holds `after`
        StudioProject? redone = history.Redo(before);

        Assert.NotNull(redone);
        int redoneClips = redone!.Clips.Count;
        Assert.Equal(2, redoneClips); // back to the two-clip state
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_InvalidatesRedo()
    {
        var history = new StudioEditHistory();
        history.Push(Project("p", (0, 0)));
        history.Undo(Project("p", (0, 0), (1, 8)));
        Assert.True(history.CanRedo);

        history.Push(Project("p", (2, 4))); // a fresh edit
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_IgnoresAnIdenticalConsecutiveSnapshot()
    {
        var history = new StudioEditHistory();
        history.Push(Project("p", (0, 0)));
        history.Push(Project("p", (0, 0))); // same content -> no second step
        Assert.Equal(1, history.UndoDepth);
    }

    [Fact]
    public void Push_DistinguishesDifferentClipPlacements()
    {
        var history = new StudioEditHistory();
        history.Push(Project("p", (0, 0)));
        history.Push(Project("p", (0, 16))); // moved -> a genuine new step
        Assert.Equal(2, history.UndoDepth);
    }

    [Fact]
    public void Depth_IsBounded_OldestDropped()
    {
        var history = new StudioEditHistory(depth: 3);
        for (int i = 0; i < 10; i++)
            history.Push(Project("p", (0, i))); // each distinct

        Assert.Equal(3, history.UndoDepth);
    }

    [Fact]
    public void Clear_DropsBothStacks()
    {
        var history = new StudioEditHistory();
        history.Push(Project("p", (0, 0)));
        history.Undo(Project("p", (0, 0), (1, 8)));
        Assert.True(history.CanRedo);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_OnEmpty_ReturnsNull()
        => Assert.Null(new StudioEditHistory().Undo(Project("p")));

    [Fact]
    public void Redo_OnEmpty_ReturnsNull()
        => Assert.Null(new StudioEditHistory().Redo(Project("p")));

    [Fact]
    public void Constructor_RejectsNonPositiveDepth()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new StudioEditHistory(depth: 0));
}
