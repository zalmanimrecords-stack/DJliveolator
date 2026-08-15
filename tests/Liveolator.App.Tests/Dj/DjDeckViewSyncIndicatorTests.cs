using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// The DJ deck must SHOW why SYNC is holding tempo only. The engine's tempo-only downgrade was correct and
/// tested, but the hint and the locked styling existed only in DjProDeckView — and both the DJ tab and LIVE
/// host <see cref="DjDeckView"/>, so on the two screens actually performed on, a deck running tempo-only
/// looked identical to one locked in the pocket and the DJ opened the fader believing the phase was held.
/// <para>Asserted against the rendered view, not just the view-model, because the defect WAS the missing
/// binding: a view-model-only test passed throughout.</para>
/// </summary>
public class DjDeckViewSyncIndicatorTests
{
    private static (Window Window, DeckViewModel Vm, FakeDispatcher Dispatcher) Deck()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);
        var view = new DjDeckView { DataContext = vm };
        var window = new Window { Content = view, Width = 700, Height = 900 };
        window.Show();
        return (window, vm, dispatcher);
    }

    private static TextBlock? HintNamed(Window window, string text)
    {
        foreach (TextBlock block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (block.Text == text)
                return block;
        }

        return null;
    }

    [AvaloniaFact]
    public void AGoodGrid_ShowsNeitherHint()
    {
        (Window window, _, FakeDispatcher dispatcher) = Deck();
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetPhaseSyncReady, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 1));

        Assert.False(HintNamed(window, "◆ GRID UNCERTAIN · TEMPO-ONLY")?.IsVisible ?? false);
        Assert.False(HintNamed(window, "◆ GRID NOT ANALYZED · TEMPO-ONLY")?.IsVisible ?? false);
    }

    [AvaloniaFact]
    public void ARefusedGrid_ShowsTheTempoOnlyHint()
    {
        (Window window, _, FakeDispatcher dispatcher) = Deck();

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetPhaseSyncReady, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));

        TextBlock? hint = HintNamed(window, "◆ GRID UNCERTAIN · TEMPO-ONLY");
        Assert.NotNull(hint);
        Assert.True(hint!.IsVisible);
    }

    [AvaloniaFact]
    public void TheSyncButton_ReadsLockedOnlyWhenThePhaseHasActuallySettled()
    {
        // "Tempo matched" and "locked in the pocket" must not look the same: the whole point of the gate is
        // that the DJ can tell what the deck is really doing before opening the fader.
        (Window window, DeckViewModel vm, FakeDispatcher dispatcher) = Deck();

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true,
                Value: (double)SyncLockState.Active, Argument: SyncLockState.Active.ToString()));
        Assert.False(vm.IsSyncLockedTight);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true,
                Value: (double)SyncLockState.Locked, Argument: SyncLockState.Locked.ToString()));

        Assert.True(vm.IsSyncLockedTight);
        Button sync = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("key") && b.Classes.Contains("locked"));
        Assert.NotNull(sync);
    }
}
