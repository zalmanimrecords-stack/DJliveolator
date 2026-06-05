using System.Windows.Input;

namespace Liveolator.App.Features.Shared;

/// <summary>A single dynamic context-menu entry: a header and the command it invokes.</summary>
public sealed record MenuActionViewModel(string Header, ICommand Command);
