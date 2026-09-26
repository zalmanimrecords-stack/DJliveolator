using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// A hot-cue pad is a momentary trigger, not a latched toggle: it must never wear the blue <c>.on</c> state
/// (owner, 2026-09-25 — a freshly-set cue read as a button stuck down). An empty slot is outlined red and a
/// stored cue is filled green, on every deck view that hosts the pads. Asserted against the rendered view
/// with the real App styles, because the defect lived in the XAML, not the view-model.
/// </summary>
public class HotCuePadStylingTests
{
    public static TheoryData<string> Hosts => new()
    {
        nameof(DeckView), nameof(DjDeckView), nameof(DjProHotCueStripView),
    };

    private static Control Host(string name) => name switch
    {
        nameof(DeckView) => new DeckView(),
        nameof(DjDeckView) => new DjDeckView(),
        _ => new DjProHotCueStripView(),
    };

    private static Color Token(string key) => (Color)Application.Current!.FindResource(key)!;

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaTheory]
    [MemberData(nameof(Hosts))]
    public void EmptyPadsAreOutlinedRed_ASetPadIsGreen_AndNoPadLatchesBlue(string host)
    {
        var dispatcher = new FakeDispatcher();
        Control view = Host(host);
        view.DataContext = new DeckViewModel(slot: 0, dispatcher);
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckHotCue, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: "1"));
        Dispatcher.UIThread.RunJobs();

        Button[] pads = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("cuepad")).ToArray();
        Button PadAt(int index) => pads.Single(p => ((HotCuePadViewModel)p.DataContext!).Index == index);

        Assert.Equal(4, pads.Length);
        Assert.DoesNotContain(pads, p => p.Classes.Contains("on"));
        Assert.Equal(Token("RedColor"), Solid(PadAt(0).BorderBrush));
        Assert.Equal(Token("CueSetColor"), Solid(PadAt(1).Background));
        Assert.Equal(Token("CueSetColor"), Solid(PadAt(1).BorderBrush));
        TextBlock setLabel = PadAt(1).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "2");
        Assert.Equal(Token("BgColor"), Solid(setLabel.Foreground)); // dark ink on green, not the global Text
        Assert.Equal(13, setLabel.FontSize);
    }
}
