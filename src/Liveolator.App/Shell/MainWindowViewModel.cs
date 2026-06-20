using System;
using System.Collections.ObjectModel;
using System.Linq;
using Liveolator.App.Features.Addons;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Mappings;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
using Liveolator.App.Features.Studio;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.Core.Settings;
using ReactiveUI;

namespace Liveolator.App.Shell;

/// <summary>
/// The application shell: the tab set and the currently selected tab. Real feature
/// view-models (e.g. Libraries) are injected — that injection is how a module connects to
/// the UI. Tabs without a module yet show a <see cref="PlaceholderViewModel"/>.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
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
        AppSettings? appSettings = null)
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
    }

    /// <summary>Top-bar telemetry: audio routing + live MIDI connectivity/activity.</summary>
    public ShellStatusViewModel Status { get; }

    /// <summary>The global OS master-volume knob shown in the top bar.</summary>
    public SystemVolumeControlViewModel SystemVolume { get; }

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
