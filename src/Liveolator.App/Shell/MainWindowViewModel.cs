using System;
using System.Collections.ObjectModel;
using System.Linq;
using Liveolator.App.Features.Addons;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Features.Mappings;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
using Liveolator.App.Features.Studio;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.Core.Audio;
using Liveolator.Core.Settings;
using ReactiveUI;

namespace Liveolator.App.Shell;

/// <summary>
/// The application shell: the tab set and the currently selected tab. Real feature
/// view-models (e.g. Libraries) are injected — that injection is how a module connects to
/// the UI. Every tab is backed by a real feature view-model.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly Features.Live.Modules.PerformanceDeckSet _decks;
    private TabItemViewModel _currentTab;

    public MainWindowViewModel(
        LibrariesViewModel libraries,
        LiveViewModel live,
        DjViewModel dj,
        StudioViewModel studio,
        VisualLibraryViewModel visualLibrary,
        AddonsViewModel addons,
        SettingsViewModel settings,
        GlobalMidiLearnCoordinator midiLearn,
        ShellStatusViewModel status,
        SystemVolumeControlViewModel systemVolume,
        AppSettings? appSettings = null,
        AudioEngineStatus? audioStatus = null)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(dj);
        ArgumentNullException.ThrowIfNull(studio);
        ArgumentNullException.ThrowIfNull(visualLibrary);
        ArgumentNullException.ThrowIfNull(addons);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(midiLearn);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(systemVolume);

        Status = status;
        MidiLearn = midiLearn;
        SystemVolume = systemVolume;
        Limiter = dj.Mixer;
        AudioEngineWarning = audioStatus?.Warning;

        // Surface live playback up to the shell so it can hold discrete responsive reflows while a deck plays
        // (see MainWindow). Forward the deck-set's change as our own so the view can bind/observe one property.
        _decks = dj.Decks;
        _decks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Features.Live.Modules.PerformanceDeckSet.AnyDeckPlaying))
                this.RaisePropertyChanged(nameof(IsAnyDeckPlaying));
        };

        // Tab labels are uppercase to match the mock (design/mockups/live-mode-clean.html), like the
        // other spartan micro-labels. The placeholder page headings keep their proper case.
        Tabs = new ObservableCollection<TabItemViewModel>
        {
            new("LIVE", live),
            new("DJ", dj),
            new("STUDIO", studio),
            new("VJ", visualLibrary),
            new("LIBRARIES", libraries),
            new("ADDONS", addons),
            new("SETTINGS", settings),
        };

        // Reopen the tab the performer left on last time (persisted by its stable label id); fall back to
        // the Live tab — the full performance surface (mock-faithful) — on first run or an unknown id.
        string? activeTabId = appSettings?.WindowLayout.Normalized().ActiveTabId;
        _currentTab = Tabs.FirstOrDefault(
            tab => string.Equals(tab.Title, activeTabId, StringComparison.OrdinalIgnoreCase)) ?? Tabs[0];

        // Re-read the catalog into the DJ-tab browser whenever the DJ tab is (re)entered, so tracks scanned
        // in LIBRARIES show up there (MediaLibrary exposes no change event to subscribe to).
        this.WhenAnyValue(x => x.CurrentTab)
            .Subscribe(tab => (tab?.Page as DjViewModel)?.Browser?.Refresh());
    }

    /// <summary>A startup warning about the realtime audio engine (e.g. a missing bass_fx that makes every
    /// track load fail), or null when healthy. Shown as a shell banner so the failure is stated up front
    /// instead of presenting decks where playback and SYNC silently do nothing.</summary>
    public string? AudioEngineWarning { get; }

    /// <summary>True when there is an audio-engine warning to show (drives the banner's visibility).</summary>
    public bool HasAudioEngineWarning => !string.IsNullOrEmpty(AudioEngineWarning);

    /// <summary>True while either deck is playing — the shell holds discrete responsive reflows until the
    /// set is paused so a resize/projector change mid-mix never jumps the layout under the DJ's hands.</summary>
    public bool IsAnyDeckPlaying => _decks.AnyDeckPlaying;

    /// <summary>Top-bar telemetry: audio routing + live MIDI connectivity/activity.</summary>
    public ShellStatusViewModel Status { get; }

    /// <summary>The global OS master-volume knob shown in the top bar.</summary>
    public SystemVolumeControlViewModel SystemVolume { get; }

    /// <summary>The DJ mixer, surfaced here so the master smart-limiter controls (CHARACTER · SMART ·
    /// CEILING) can live in the global top bar next to the OS-volume knob rather than inside the DJ
    /// mixer frame. Only the DJ mixer carries a limiter; it stays bound across every tab.</summary>
    public MixerViewModel Limiter { get; }

    public GlobalMidiLearnCoordinator MidiLearn { get; }

    public ObservableCollection<TabItemViewModel> Tabs { get; }

    public TabItemViewModel CurrentTab
    {
        get => _currentTab;
        set => this.RaiseAndSetIfChanged(ref _currentTab, value);
    }

    /// <summary>The stable id (tab label) of the currently selected tab — persisted as the layout's
    /// active tab so the app reopens here on the next launch.</summary>
    public string CurrentTabId => CurrentTab.Title;

    /// <summary>Move to the next tab, wrapping from the last back to the first (Tab key).</summary>
    public void SelectNextTab() => StepTab(+1);

    /// <summary>Move to the previous tab, wrapping from the first to the last (Shift+Tab).</summary>
    public void SelectPreviousTab() => StepTab(-1);

    /// <summary>Jump straight to the tab at the given 1-based position (number keys 1..N). Out-of-range
    /// numbers are ignored so a stray key press can never move the selection somewhere unexpected.</summary>
    public void SelectTabByNumber(int number)
    {
        int index = number - 1;
        if (index >= 0 && index < Tabs.Count)
        {
            CurrentTab = Tabs[index];
        }
    }

    public void CancelMidiLearn() => MidiLearn.Cancel();

    private void StepTab(int direction)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        int current = Tabs.IndexOf(CurrentTab);
        // Modular step that stays non-negative for either direction (+1 forward, -1 back).
        int next = ((current + direction) % Tabs.Count + Tabs.Count) % Tabs.Count;
        CurrentTab = Tabs[next];
    }
}
