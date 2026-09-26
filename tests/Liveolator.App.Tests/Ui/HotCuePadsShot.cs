using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;

namespace Liveolator.App.Tests.Ui;

// Hot-cue pad states side by side — empty (red outline), set, labelled + coloured, and an auto suggestion —
// on the DJ PRO strip and the DJ deck. Eyeball legibility in artifacts/ui-shots/hot-cue-pads.png.
public class HotCuePadsShot
{
    [AvaloniaFact]
    public void Render_hot_cue_pad_states_to_png()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(0, dispatcher);
        var window = new Window
        {
            Width = 460,
            Height = 700,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new DjProHotCueStripView { DataContext = vm },
                    new DjDeckView { DataContext = vm },
                },
            },
        };
        window.Show();

        RaiseCue(dispatcher, 1, new HotCueInfo(IsSet: true));
        RaiseCue(dispatcher, 2, new HotCueInfo(IsSet: true, Label: "Drop", Color: 0xE5403A));
        RaiseCue(dispatcher, 3, new HotCueInfo(IsSet: true, IsAuto: true));
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "hot-cue-pads.png");
        window.CaptureRenderedFrame()?.Save(outputPath);

        Assert.True(File.Exists(outputPath));
    }

    private static void RaiseCue(FakeDispatcher dispatcher, int index, HotCueInfo info) =>
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckHotCue, 0,
            new ActionFeedbackState(IsActive: info.IsSet, IsAvailable: true, Value: 0,
                Argument: HotCueFeedback.Encode(index, info)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? System.AppContext.BaseDirectory;
    }
}
