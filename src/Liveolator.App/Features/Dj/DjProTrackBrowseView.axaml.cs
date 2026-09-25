using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Liveolator.App.Features.Dj;

public partial class DjProTrackBrowseView : UserControl
{
    // Track browse-and-load commands, injected per deck by DjProView (which owns the shared browser).
    // The view stays browser-agnostic — it only invokes whatever command it was handed.
    public static readonly StyledProperty<ICommand?> PrevCommandProperty =
        AvaloniaProperty.Register<DjProTrackBrowseView, ICommand?>(nameof(PrevCommand));

    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<DjProTrackBrowseView, ICommand?>(nameof(NextCommand));

    public DjProTrackBrowseView() => InitializeComponent();

    /// <summary>Loads the previous track from the browser's current list onto this deck.</summary>
    public ICommand? PrevCommand
    {
        get => GetValue(PrevCommandProperty);
        set => SetValue(PrevCommandProperty, value);
    }

    /// <summary>Loads the next track from the browser's current list onto this deck.</summary>
    public ICommand? NextCommand
    {
        get => GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }
}
