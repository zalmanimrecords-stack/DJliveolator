using Liveolator.App.Shell;
using Liveolator.Core.Extensions;

namespace Liveolator.App.Features.Settings;

public sealed class ExtensionItemViewModel : ViewModelBase
{
    public ExtensionItemViewModel(InstalledExtension extension) => Extension = extension;

    public InstalledExtension Extension { get; }
    public string PackageId => Extension.Manifest.PackageId;
    public string Version => Extension.Manifest.Version;
    public string Publisher => Extension.Manifest.Publisher;
    public string Content => Extension.Manifest.Content.ToString();
    public bool IsEnabled => Extension.IsEnabled;
    public string State => IsEnabled ? "Enabled" : "Disabled";
}
