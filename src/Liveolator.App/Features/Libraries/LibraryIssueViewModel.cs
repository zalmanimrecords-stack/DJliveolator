using Liveolator.App.Shell;
using Liveolator.Core.Library.Doctor;

namespace Liveolator.App.Features.Libraries;

public sealed class LibraryIssueViewModel : ViewModelBase
{
    public LibraryIssueViewModel(LibraryIssue issue)
        => Issue = issue ?? throw new ArgumentNullException(nameof(issue));

    public LibraryIssue Issue { get; }

    public LibraryIssueKind Kind => Issue.Kind;
    public LibraryRepairConfidence Confidence => Issue.Confidence;
    public string Title => Issue.Title;
    public string Path => Issue.Path;
    public string Message => Issue.Message;
    public IReadOnlyList<string> RelatedPaths => Issue.RelatedPaths;
    public string RelatedText => Issue.RelatedPaths.Count <= 1 ? string.Empty : string.Join(" | ", Issue.RelatedPaths);
}

