using Liveolator.Core.Update;

namespace Liveolator.App.Features.Update;

/// <summary>
/// Presents the "a newer version is available" dialog and resolves to the user's choice. Abstracted from
/// the Avalonia window so <see cref="StartupUpdateChecker"/> is unit-testable without UI.
/// </summary>
public interface IUpdatePrompt
{
    /// <summary>Shows the prompt for <paramref name="manifest"/> against the running <paramref name="currentVersion"/>.</summary>
    Task<UpdateDialogChoice> PromptAsync(UpdateManifest manifest, string currentVersion);
}
