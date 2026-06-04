using Liveolator.App.Shell;

namespace Liveolator.App.Features.Shared;

/// <summary>Stand-in page for a tab whose module has not been built yet.</summary>
public sealed class PlaceholderViewModel : ViewModelBase
{
    public PlaceholderViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }
}
