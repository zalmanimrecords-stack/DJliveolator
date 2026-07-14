using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Liveolator.App.Layout;

namespace Liveolator.App.Shell;

public partial class MainWindow : Window
{
    // The four mutually-exclusive responsive style classes carried on the Window; descendant style
    // selectors (Window.compact ..., Window.wide ...) react to whichever one is set.
    private static readonly string[] SizeClassNames = { "compact", "standard", "wide", "ultra" };

    private LayoutSizeClass _sizeClass = LayoutSizeClass.Standard;
    private MainWindowViewModel? _observedViewModel;

    /// <summary>Test seam: overrides the "is a deck playing" probe used by the reflow gate, so the
    /// no-reflow-while-playing rule can be exercised without composing a full playing-deck view-model.</summary>
    internal Func<bool>? PlayingProbeForTests { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        // Tab cycles the app's screens (Shift+Tab goes back). Handle the tunnelling phase so the
        // window sees the key before the focused control's default Tab focus-traversal consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Watch playback (via the shell VM) so a tier change deferred while a deck was playing is flushed
        // the moment the set is paused.
        DataContextChanged += OnDataContextChanged;

        // Establish a self-consistent initial tier LAST (after InitializeComponent has wired ClientSize):
        // pin the field + applied class to the design baseline, then resolve once from the real size so the
        // field and the style class can never diverge (an early ClientSize change during init won't strand a
        // stale class). Subsequent resizes flow through OnPropertyChanged.
        _sizeClass = LayoutSizeClass.Standard;
        ApplySizeClass(_sizeClass);
        UpdateSizeClass(ClientSize.Width);
    }

    /// <summary>The active responsive tier (for tests/diagnostics).</summary>
    internal LayoutSizeClass CurrentSizeClass => _sizeClass;

    // React to window resizes (drag, maximize, move to a projector) by re-resolving the responsive tier.
    // This only swaps a style class + a scale resource — it never touches audio, reloads tracks, or rebuilds
    // view-models, so a resize mid-set cannot interrupt playback (a hard live-performance invariant).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
            UpdateSizeClass(ClientSize.Width);
    }

    // Resolve the tier for the current width (with hysteresis) and, if it changed, apply it — UNLESS a deck
    // is playing, in which case the discrete change is held until playback stops (OnPlaybackChanged flushes
    // it). Continuous column flex (the * grid columns) is unaffected and keeps tracking the width meanwhile.
    internal void UpdateSizeClass(double width)
    {
        var next = LayoutSizeClassResolver.Resolve(width, _sizeClass);
        if (next == _sizeClass || IsDeckPlaying())
            return;
        _sizeClass = next;
        ApplySizeClass(next);
    }

    private bool IsDeckPlaying()
        => PlayingProbeForTests?.Invoke() ?? (DataContext as MainWindowViewModel)?.IsAnyDeckPlaying ?? false;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When the last deck stops, re-resolve from the current width so any tier change deferred during
        // playback now takes effect (no-op if the width never crossed a boundary).
        if (e.PropertyName == nameof(MainWindowViewModel.IsAnyDeckPlaying) && !IsDeckPlaying())
            UpdateSizeClass(ClientSize.Width);
    }

    private void ApplySizeClass(LayoutSizeClass cls)
    {
        string target = LayoutScale.StyleClass(cls);
        foreach (var name in SizeClassNames)
        {
            bool wanted = name == target;
            if (wanted && !Classes.Contains(name))
                Classes.Add(name);
            else if (!wanted && Classes.Contains(name))
                Classes.Remove(name);
        }

        // Quantized scale multiplier for size-driven (never transform-driven) control/font scaling.
        Resources["UiScale"] = LayoutScale.For(cls);

        // Rewrite the size design tokens for this tier. Controls bind to these by key as DynamicResources,
        // so knobs/faders/readouts grow on Wide/Ultra and stay at the baseline (live floor) on Compact/Standard.
        foreach (var (key, value) in LayoutSizeTokens.For(cls))
            Resources[key] = value;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && DataContext is MainWindowViewModel escapeVm
            && escapeVm.MidiLearn.IsEnabled)
        {
            escapeVm.CancelMidiLearn();
            e.Handled = true;
            return;
        }

        // Bare number keys 1..N jump straight to the matching tab (1 = first tab). Like Tab cycling, this
        // is suppressed while a text/list control owns focus so digits typed into a field aren't hijacked,
        // and only for an unmodified press so Ctrl/Alt/Shift+digit shortcuts elsewhere are left alone.
        if (e.KeyModifiers == KeyModifiers.None
            && DataContext is MainWindowViewModel numberVm
            && DigitFromKey(e.Key) is int tabNumber
            && ShouldCycleScreensOnTab(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            numberVm.SelectTabByNumber(tabNumber);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Don't hijack Tab while focus is in an editable/list control — the user needs it there for
        // standard field-to-field focus traversal (e.g. the Settings form, the library filter bar).
        // Consuming it unconditionally broke keyboard navigation app-wide (docs/19 accessibility).
        if (!ShouldCycleScreensOnTab(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            vm.SelectPreviousTab();
        }
        else
        {
            vm.SelectNextTab();
        }

        e.Handled = true;
    }

    // Bare Tab cycles the app's screens, but only when focus is on chrome or nothing — never when a
    // text-editing or list control owns it, since those need Tab for their own focus traversal.
    // Pure + static so the decision is unit-testable without spinning up a window and focus tree.
    internal static bool ShouldCycleScreensOnTab(IInputElement? focused)
        => focused is not (TextBox or ComboBox or AutoCompleteBox or NumericUpDown);

    // Maps a top-row or numpad digit key (1..9) to its 1-based tab number; null for any other key.
    // Pure + static so the mapping is unit-testable without a window.
    internal static int? DigitFromKey(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
        _ => null,
    };

    private void OnFullScreenClick(object? sender, RoutedEventArgs e)
        => ToggleFullScreen();

    internal void ToggleFullScreen() => SetFullScreen(WindowState != WindowState.FullScreen);

    // Applies a full-screen / windowed state and keeps the toggle button's label in step. Shared by the
    // F11/button toggle and the startup layout restore so both routes stay consistent (decorations + label).
    internal void SetFullScreen(bool fullScreen)
    {
        SystemDecorations = fullScreen ? SystemDecorations.None : SystemDecorations.Full;
        WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;
        FullScreenButton.Content = fullScreen ? "WINDOW" : "FULL";
    }
}
