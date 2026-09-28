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
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// DJ PRO compaction (owner, 2026-09-26): the deck printed its BPM and key twice (the track line plus separate
/// readouts), and the mixer's CUE buttons sat in their own row. Now the yellow track line is the only BPM/key
/// readout, and each headphone cue is a "C" key directly under its crossfader A/B snap, the same size.
/// </summary>
public class DjProCompactLayoutTests
{
    private static Window Show(Control view)
    {
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Color Token(string key) => (Color)Application.Current!.FindResource(key)!;

    [AvaloniaFact]
    public void TheDeck_ShowsBpmAndKeyOnlyInTheYellowTrackLine()
    {
        Window window = Show(new DjProDeckView { DataContext = new DeckViewModel(slot: 0, new FakeDispatcher()) });
        TextBlock[] texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();

        Assert.DoesNotContain(texts, t => t.Classes.Contains("bpm"));
        Assert.DoesNotContain(texts, t => ToolTip.GetTip(t) as string == "Musical key");
        TextBlock trackLine = Assert.Single(texts, t => t.Classes.Contains("deckmeta"));
        Assert.Equal(Token("DeckMetaColor"), Assert.IsAssignableFrom<ISolidColorBrush>(trackLine.Foreground).Color);
    }

    [AvaloniaFact]
    public void EachMixerCue_IsACKeyDirectlyUnderItsCrossfaderSnap_AtTheSameSize()
    {
        var mixer = new MixerViewModel(new FakeDispatcher());
        Window window = Show(new DjProMixerView { DataContext = mixer });
        Button[] buttons = window.GetVisualDescendants().OfType<Button>().ToArray();

        Assert.DoesNotContain(buttons, b => b.Content as string == "CUE");
        AssertUnder(buttons.Single(b => b.Content as string == "A"),
            buttons.Single(b => b.Command == mixer.CueACommand));
        AssertUnder(buttons.Single(b => b.Content as string == "B"),
            buttons.Single(b => b.Command == mixer.CueBCommand));
    }

    private static void AssertUnder(Button snap, Button cue)
    {
        Assert.Equal("C", cue.Content);
        Assert.Same(snap.Parent, cue.Parent);
        Assert.Equal(Grid.GetColumn(snap), Grid.GetColumn(cue));
        Assert.Equal(Grid.GetRow(snap) + 1, Grid.GetRow(cue));
        Assert.Equal(snap.Bounds.Width, cue.Bounds.Width, precision: 1);
        Assert.Equal(snap.Bounds.Height, cue.Bounds.Height, precision: 1);
    }
}
