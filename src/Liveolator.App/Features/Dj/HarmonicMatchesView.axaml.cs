using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Liveolator.App.Features.Dj;

public partial class HarmonicMatchesView : UserControl
{
    public HarmonicMatchesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
