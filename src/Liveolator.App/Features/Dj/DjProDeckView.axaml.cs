using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Liveolator.App.Features.Dj;

public partial class DjProDeckView : UserControl
{
    // Track browse-and-load commands, injected per deck by DjProView (which owns the shared browser).
    // The deck view stays browser-agnostic — it only invokes whatever command it was handed.
    public static readonly StyledProperty<ICommand?> BrowsePrevCommandProperty =
        AvaloniaProperty.Register<DjProDeckView, ICommand?>(nameof(BrowsePrevCommand));

    public static readonly StyledProperty<ICommand?> BrowseNextCommandProperty =
        AvaloniaProperty.Register<DjProDeckView, ICommand?>(nameof(BrowseNextCommand));

    public DjProDeckView() => InitializeComponent();

    /// <summary>Loads the previous track from the browser's current list onto this deck.</summary>
    public ICommand? BrowsePrevCommand
    {
        get => GetValue(BrowsePrevCommandProperty);
        set => SetValue(BrowsePrevCommandProperty, value);
    }

    /// <summary>Loads the next track from the browser's current list onto this deck.</summary>
    public ICommand? BrowseNextCommand
    {
        get => GetValue(BrowseNextCommandProperty);
        set => SetValue(BrowseNextCommandProperty, value);
    }
}
