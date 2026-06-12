using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Liveolator.App.Features.Studio;

public partial class StudioView : UserControl
{
    private bool _initialized;

    public StudioView() => InitializeComponent();

    // Load the library snapshot + saved-set list the first time the tab is shown. Fire-and-forget:
    // the VM guards its own store calls and surfaces failures on Status, so a load error never throws
    // into the UI thread here (global #16/#26).
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_initialized || DataContext is not StudioViewModel vm)
            return;
        _initialized = true;
        await vm.InitializeAsync();
    }
}
