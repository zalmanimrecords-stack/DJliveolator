using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Liveolator.App.Layout;

namespace Liveolator.App.Features.Dj;

public partial class DjProView : UserControl
{
    // ponytail: mirrors DjView.axaml.cs's browser-row collapse. Two copies is the threshold; extract a
    // shared responsive-browser-row behaviour only if a third DJ surface needs it.
    private static readonly string[] TierClasses = { "compact", "standard", "wide", "ultra" };

    private TopLevel? _topLevel;

    public DjProView() => InitializeComponent();

    // The browser band tracks the shell Window's responsive tier (a style class): 0 on small/laptop tiers
    // (the console + racks keep the height to stay one-screen), a star band on wide/4K. Re-applied on every
    // class change (resize) without coupling this view to the size-class engine.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = this.GetVisualRoot() as TopLevel;
        if (_topLevel is not null)
            ((INotifyCollectionChanged)_topLevel.Classes).CollectionChanged += OnTierClassesChanged;
        ApplySplit();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            ((INotifyCollectionChanged)_topLevel.Classes).CollectionChanged -= OnTierClassesChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTierClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplySplit();

    private void ApplySplit()
    {
        var grid = this.FindControl<Grid>("RootGrid");
        if (grid is null || grid.RowDefinitions.Count < 3)
            return;

        // Row 0 = waveforms, Row 1 = performance area, Row 2 = the collapsible browser band.
        string? tier = _topLevel is null ? null : TierClasses.FirstOrDefault(_topLevel.Classes.Contains);
        double share = DjBrowserLayout.RowShare(LayoutScale.FromStyleClass(tier));
        grid.RowDefinitions[2].Height = share <= 0
            ? new GridLength(0)
            : new GridLength(share, GridUnitType.Star);
    }
}
