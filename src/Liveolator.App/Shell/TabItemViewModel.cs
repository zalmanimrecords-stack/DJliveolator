namespace Liveolator.App.Shell;

/// <summary>One app-level tab: a title and the page view-model shown when it is selected.</summary>
public sealed class TabItemViewModel : ViewModelBase
{
    public TabItemViewModel(string title, ViewModelBase page)
    {
        Title = title;
        Page = page;
    }

    public string Title { get; }

    public ViewModelBase Page { get; }
}
