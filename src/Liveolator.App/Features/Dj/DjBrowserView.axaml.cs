using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Liveolator.App.Features.Dj;

public partial class DjBrowserView : UserControl
{
    public DjBrowserView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Double-click a row to load it onto the free deck (only when exactly one deck is playing — the VM's
    // FreeDeckSlot rule keeps this unambiguous so a wrong-deck load can't happen by accident mid-set).
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DjBrowserViewModel vm)
            vm.LoadToFreeDeck();
    }
}
