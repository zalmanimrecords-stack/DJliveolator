using System.Collections.ObjectModel;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
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
        SettingsViewModel settings,
        ShellStatusViewModel status)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(dj);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(status);

        Status = status;

        // Tab labels are uppercase to match the mock (design/mockups/live-mode-clean.html), like the
        // other spartan micro-labels. The placeholder page headings keep their proper case.
        Tabs = new ObservableCollection<TabItemViewModel>
        {
            new("LIVE", live),
            new("DJ", dj),
            new("VJ", new PlaceholderViewModel("VJ", "Visual compositor — coming soon.")),
            new("LIBRARIES", libraries),
            new("MAPPINGS", new PlaceholderViewModel("Mappings", "MIDI learn & devices — coming soon.")),
            new("SETTINGS", settings),
        };

        // Open the Live tab — the full performance surface (mock-faithful) is the app's centrepiece.
        _currentTab = Tabs[0];
    }

    /// <summary>Top-bar telemetry: audio routing + live MIDI connectivity/activity.</summary>
    public ShellStatusViewModel Status { get; }

    public ObservableCollection<TabItemViewModel> Tabs { get; }

    public TabItemViewModel CurrentTab
    {
        get => _currentTab;
        set => this.RaiseAndSetIfChanged(ref _currentTab, value);
    }
}
