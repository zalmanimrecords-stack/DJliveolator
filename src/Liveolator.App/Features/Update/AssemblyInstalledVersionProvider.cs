using Liveolator.App.Diagnostics;
using Liveolator.Core.Update;

namespace Liveolator.App.Features.Update;

/// <summary>
/// <see cref="IInstalledVersionProvider"/> backed by the running executable's assembly metadata, reusing
/// the same <see cref="AppVersionInfo"/> the Diagnostics tab shows so the version reported here always
/// matches what the user sees there.
/// </summary>
public sealed class AssemblyInstalledVersionProvider : IInstalledVersionProvider
{
    public string CurrentVersion { get; } = AppVersionInfo.FromEntryAssembly().Version;
}
