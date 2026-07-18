using System.IO;
using System.Reactive.Concurrency;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using ReactiveUI;

namespace Liveolator.App.Tests.Ui;

// Render smoke test for the DJ PRO performance row (decks + mixer + stem/fx racks). Confirms the surface
// composes and lets the owner eyeball deck A/B symmetry and clipping in artifacts/ui-shots/dj-pro-perf.png.
public class DjProShot
{
    [AvaloniaFact]
    public void Render_dj_pro_performance_row_to_png()
    {
        var dispatcher = new FakeDispatcher();
        Window window = BuildPerformanceRow(dispatcher, out PerformanceDeckSet decks);
        using (decks)
        {
            window.Show();
            // Populate BPM/key/pitch/loop so the readouts and loop label render with real content, not blanks —
            // this is where the old stacked-below-jog layout clipped the bottom row against the STEMS rack.
            Play(dispatcher, slot: 0, bpm: 128.0);
            Play(dispatcher, slot: 1, bpm: 128.0);
            Dispatcher.UIThread.RunJobs();

            Capture(window, "dj-pro-perf.png");
        }
    }

    // Renders the sync mode / lock-state / master-follower / half-time feedback (SYNC-BEHAVIOR-SPEC §4/§11):
    // a 140 master (deck A, MASTER chip) with a 70 follower (deck B) Sync-Locked at half time — green SYNC
    // "in the pocket" + 2x/½x octave tags. Eyeball the colours/badges in artifacts/ui-shots/dj-pro-sync-states.png.
    [AvaloniaFact]
    public void Render_dj_pro_sync_states_to_png()
    {
        var dispatcher = new FakeDispatcher();
        Window window = BuildPerformanceRow(dispatcher, out PerformanceDeckSet decks);
        using (decks)
        {
            window.Show();
            Play(dispatcher, slot: 0, bpm: 140.0); // master
            Play(dispatcher, slot: 1, bpm: 70.0);  // follower, half time

            // Deck B follows deck A in Sync Lock, fully locked → GREEN SYNC; deck A becomes MASTER (derived
            // cross-deck). The 140-vs-70 beatmatch tags each deck with its 2x / ½x octave relationship.
            dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncToggle, 1, Sync(SyncLockState.Locked));
            Dispatcher.UIThread.RunJobs();

            Capture(window, "dj-pro-sync-states.png");
        }
    }

    // Renders the grid-confidence downgrade (§7): deck B on Tempo Sync with a low-confidence grid — amber
    // T·SYNC + the "grid uncertain · tempo-only" hint. artifacts/ui-shots/dj-pro-grid-uncertain.png.
    [AvaloniaFact]
    public void Render_dj_pro_grid_uncertain_to_png()
    {
        var dispatcher = new FakeDispatcher();
        Window window = BuildPerformanceRow(dispatcher, out PerformanceDeckSet decks);
        using (decks)
        {
            window.Show();
            Play(dispatcher, slot: 0, bpm: 128.0);
            Play(dispatcher, slot: 1, bpm: 128.0);

            dispatcher.RaiseFeedback(PerformanceActionKind.DeckTempoSyncToggle, 1, Sync(SyncLockState.Active));
            dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetPhaseSyncReady, 1,
                new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));
            Dispatcher.UIThread.RunJobs();

            Capture(window, "dj-pro-grid-uncertain.png");
        }
    }

    private static Window BuildPerformanceRow(FakeDispatcher dispatcher, out PerformanceDeckSet decks)
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        decks = new PerformanceDeckSet(dispatcher, library: null, deckTransportEnabled: true);

        var perf = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };

        var colA = Column(new DjProDeckView { DataContext = decks.DeckA },
                          new DeckStemRackView { DataContext = new DeckStemRackViewModel(dispatcher, 0) },
                          new DeckFxRackView { DataContext = new DeckFxRackViewModel(dispatcher, 0) },
                          new Thickness(0, 0, 10, 0));
        Grid.SetColumn(colA, 0);

        var mixer = new DjProMixerView { DataContext = decks.Mixer, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(mixer, 1);

        var colB = Column(new DjProDeckView { DataContext = decks.DeckB },
                          new DeckStemRackView { DataContext = new DeckStemRackViewModel(dispatcher, 1) },
                          new DeckFxRackView { DataContext = new DeckFxRackViewModel(dispatcher, 1) },
                          new Thickness(0));
        Grid.SetColumn(colB, 2);

        perf.Children.Add(colA);
        perf.Children.Add(mixer);
        perf.Children.Add(colB);

        return new Window
        {
            // Narrow logical width — matches the Surface's high-DPI (150-200%) scaling, where the deck
            // panels are tight and the transport/loop button text is what clips.
            Width = 1280,
            Height = 720,
            Content = new Border { Padding = new Thickness(16), Child = perf },
        };
    }

    private static void Capture(Window window, string fileName)
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, fileName);
        window.CaptureRenderedFrame()?.Save(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    private static ActionFeedbackState Sync(SyncLockState state)
        => new(IsActive: state != SyncLockState.Off, IsAvailable: true, Value: (double)state, Argument: state.ToString());

    private static void Play(FakeDispatcher dispatcher, int slot, double bpm)
    {
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: bpm, Argument: "60|200"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
    }

    private static Grid Column(Control deck, Control stems, Control fx, Thickness margin)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = margin };
        Grid.SetRow(deck, 0);
        stems.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(stems, 1);
        fx.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(fx, 2);
        grid.Children.Add(deck);
        grid.Children.Add(stems);
        grid.Children.Add(fx);
        return grid;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
