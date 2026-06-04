using System.Collections.ObjectModel;
using Liveolator.App.Features.Libraries;
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

    public MainWindowViewModel(LibrariesViewModel libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);

        Tabs = new ObservableCollection<TabItemViewModel>
        {
            new("Live", new PlaceholderViewModel("Live", "Combined performance view — coming soon.")),
            new("DJ", new PlaceholderViewModel("DJ", "Two-deck DJ workspace — coming soon.")),
            new("VJ", new PlaceholderViewModel("VJ", "Visual compositor — coming soon.")),
            new("Libraries", libraries),
            new("Mappings", new PlaceholderViewModel("Mappings", "MIDI learn & devices — coming soon.")),
            new("Settings", new PlaceholderViewModel("Settings", "Preferences — coming soon.")),
        };

        // Open the wired Libraries tab so the running app shows a real module end-to-end.
        _currentTab = Tabs[3];
    }

    public ObservableCollection<TabItemViewModel> Tabs { get; }

    public TabItemViewModel CurrentTab
    {
        get => _currentTab;
        set => this.RaiseAndSetIfChanged(ref _currentTab, value);
    }
}
