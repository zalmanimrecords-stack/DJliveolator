using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Liveolator.App.Controls;

/// <summary>
/// The one deck-waveform module: a <see cref="WaveformStrip"/> pre-wired with the standard 3-band brushes,
/// beat-grid colours and kick-forward body scale, so every tab (DJ, DJ PRO, …) shows the IDENTICAL waveform
/// from one definition instead of duplicating ~15 lines of bindings per deck per view. The host binds the
/// control's DataContext to a deck view-model and sets only the layout knobs (<see cref="Folded"/> /
/// <see cref="CombAtTop"/>) for the butterfly stacking.
/// </summary>
public partial class DeckWaveform : UserControl
{
    /// <summary>Draw the wave single-sided (folded) growing away from the comb — the stacked A/B butterfly.</summary>
    public static readonly StyledProperty<bool> FoldedProperty =
        AvaloniaProperty.Register<DeckWaveform, bool>(nameof(Folded));

    /// <summary>Put the beat comb at the TOP of the strip (the lower deck of a stacked pair) so the two
    /// decks' combs meet in the middle.</summary>
    public static readonly StyledProperty<bool> CombAtTopProperty =
        AvaloniaProperty.Register<DeckWaveform, bool>(nameof(CombAtTop));

    public bool Folded { get => GetValue(FoldedProperty); set => SetValue(FoldedProperty, value); }
    public bool CombAtTop { get => GetValue(CombAtTopProperty); set => SetValue(CombAtTopProperty, value); }

    public DeckWaveform() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
