using Liveolator.App.Shell;
using Liveolator.Core.Extensions;

namespace Liveolator.App.Features.Live.Modules;

public sealed class VisualAddonViewModel : ViewModelBase
{
    public VisualAddonViewModel(InstalledExtension extension)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
    }

    public InstalledExtension Extension { get; }
    public string PackageId => Extension.Manifest.PackageId;
    public string Version => Extension.Manifest.Version;
    public bool IsEnabled => Extension.IsEnabled;
    public string State => IsEnabled ? "ON" : "OFF";
}
