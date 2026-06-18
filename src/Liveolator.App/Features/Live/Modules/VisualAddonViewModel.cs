using Liveolator.App.Shell;
using Liveolator.Core.Extensions;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// One row in the LIVE add-ons surface. It represents either an installed extension package (which can be
/// enabled/disabled via the installer) or a user FRKTL preset (a <c>.frktl</c> file, always active, listed
/// here so the operator sees the whole visual vocabulary in one flat place). <see cref="CanToggle"/>
/// distinguishes the two so the TOGGLE button only acts on real packages.
/// </summary>
public sealed class VisualAddonViewModel : ViewModelBase
{
    private VisualAddonViewModel(
        string packageId, string version, string state, bool canToggle, bool isEnabled, InstalledExtension? extension)
    {
        PackageId = packageId;
        Version = version;
        State = state;
        CanToggle = canToggle;
        IsEnabled = isEnabled;
        Extension = extension;
    }

    /// <summary>An installed extension package: TOGGLE enables/disables it through the installer.</summary>
    public static VisualAddonViewModel ForExtension(InstalledExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return new VisualAddonViewModel(
            extension.Manifest.PackageId,
            extension.Manifest.Version,
            extension.IsEnabled ? "ON" : "OFF",
            canToggle: true,
            isEnabled: extension.IsEnabled,
            extension);
    }

    /// <summary>
    /// A user FRKTL preset (doc 29) surfaced in the add-ons list. It lives as a <c>.frktl</c> file and is
    /// always active, so it is listed for visibility but is not toggleable from here.
    /// </summary>
    public static VisualAddonViewModel ForFrktlPreset(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new VisualAddonViewModel(
            name, version: string.Empty, state: "FRKTL", canToggle: false, isEnabled: true, extension: null);
    }

    /// <summary>The backing extension when this row is a package; null for a FRKTL preset row.</summary>
    public InstalledExtension? Extension { get; }
    public string PackageId { get; }
    public string Version { get; }
    public bool IsEnabled { get; }
    public string State { get; }

    /// <summary>True when TOGGLE applies (an extension package); false for an always-active FRKTL preset.</summary>
    public bool CanToggle { get; }
}
