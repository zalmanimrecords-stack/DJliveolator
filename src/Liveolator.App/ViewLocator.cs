using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Liveolator.App.Shell;

namespace Liveolator.App;

/// <summary>
/// Resolves a view for a view-model by naming convention: <c>…FooViewModel</c> →
/// <c>…FooView</c> in the same namespace. Views are plain controls with a parameterless
/// constructor; their <see cref="StyledElement.DataContext"/> is the view-model instance, so
/// no DI is needed here (modules are injected into the view-models themselves).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "(no view-model)" };

        string viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? viewType = Type.GetType(viewName);

        return viewType is null
            ? new TextBlock { Text = $"View not found: {viewName}" }
            : (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
