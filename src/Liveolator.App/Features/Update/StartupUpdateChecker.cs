using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Update;
using Microsoft.Extensions.Logging;

namespace Liveolator.App.Features.Update;

/// <summary>
/// Orchestrates the startup update check: load the preference, fetch the published manifest, apply the
/// pure <see cref="UpdateAvailabilityChecker"/> decision, and — only when a newer, non-skipped build
/// exists — prompt the user and act on their choice (open the download, persist a skip, or defer).
/// </summary>
/// <remarks>
/// Holds no Avalonia types (the UI is behind <see cref="IUpdatePrompt"/> / <see cref="IUrlOpener"/>) so
/// it unit-tests with fakes. The whole flow is best-effort: any failure is logged and swallowed so a
/// failed check never blocks or crashes startup (global standards #16/#26). The fetch is awaited without
/// <c>ConfigureAwait(false)</c> so the continuation (and therefore the prompt) resumes on the UI thread.
/// </remarks>
public sealed class StartupUpdateChecker
{
    private readonly IUpdateManifestSource _source;
    private readonly IInstalledVersionProvider _version;
    private readonly ISettingsStore _store;
    private readonly IUpdatePrompt _prompt;
    private readonly IUrlOpener _urlOpener;
    private readonly ILogger<StartupUpdateChecker> _logger;

    public StartupUpdateChecker(
        IUpdateManifestSource source,
        IInstalledVersionProvider version,
        ISettingsStore store,
        IUpdatePrompt prompt,
        IUrlOpener urlOpener,
        ILogger<StartupUpdateChecker> logger)
    {
        _source = source;
        _version = version;
        _store = store;
        _prompt = prompt;
        _urlOpener = urlOpener;
        _logger = logger;
    }

    /// <summary>Runs the check once. Safe to fire-and-forget from app startup.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AppSettings settings = await _store.LoadAsync(cancellationToken);
            if (!settings.Updates.CheckOnStartup)
                return;

            UpdateManifest? manifest = await _source.FetchAsync(cancellationToken);
            UpdateCheckResult result = UpdateAvailabilityChecker.Evaluate(
                _version.CurrentVersion, manifest, settings.Updates.SkippedVersion);
            if (!result.IsUpdateAvailable || result.Manifest is not { } available)
                return;

            UpdateDialogChoice choice = await _prompt.PromptAsync(available, _version.CurrentVersion);
            await ApplyChoiceAsync(choice, available, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup update check failed; continuing without it.");
        }
    }

    private async Task ApplyChoiceAsync(
        UpdateDialogChoice choice, UpdateManifest manifest, AppSettings settings, CancellationToken cancellationToken)
    {
        switch (choice)
        {
            case UpdateDialogChoice.Download:
                _urlOpener.Open(manifest.DownloadUrl);
                break;

            case UpdateDialogChoice.Skip:
                AppSettings updated = settings with
                {
                    Updates = settings.Updates with { SkippedVersion = manifest.Version },
                };
                await _store.SaveAsync(updated, cancellationToken);
                break;

            case UpdateDialogChoice.Later:
            default:
                break;
        }
    }
}
