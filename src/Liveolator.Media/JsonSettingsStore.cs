using System.Text.Json;
using Liveolator.Core.Audio;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;

namespace Liveolator.Media;

/// <summary>
/// Versioned on-disk shape of the application settings (a single <c>settings.json</c>). New optional
/// fields (e.g. the capture-source choice) are added as nullable so an older file that omits them still
/// deserializes — the version stays at <see cref="CurrentVersion"/> as long as the additions are
/// backward-compatible (a missing field reads as null), preserving existing configs (global #20/#22).
/// </summary>
public sealed record SettingsSnapshot(
    int Version,
    string? OutputDeviceId,
    int BufferMilliseconds,
    string? MidiControllerInputName,
    string? MidiFeedbackOutputName,
    string? CaptureDeviceId = null,
    CaptureSourceKind? CaptureSource = null,
    bool DeveloperMode = false,
    string? ActiveUiThemeId = null,
    double? WaveformZoomSeconds = null,
    double? NudgeSeconds = null,
    string? MinimumLogLevel = null,
    string? VuMeterBackgroundImagePath = null,
    int? VuMeterNeedleOrigin = null,
    string? ActiveKnobSkinId = null,
    string? ActiveSliderSkinId = null,
    string? ActiveTabId = null,
    double? WindowWidth = null,
    double? WindowHeight = null,
    double? WindowX = null,
    double? WindowY = null,
    bool? WindowIsFullScreen = null,
    int? AcceptedTermsVersion = null,
    bool? CheckForUpdatesOnStartup = null,
    string? SkippedUpdateVersion = null)
{
    public const int CurrentVersion = 2;
}

/// <summary>
/// Persists <see cref="AppSettings"/> as a single JSON file at <c>&lt;root&gt;/settings.json</c>
/// (doc 12/13). Mirrors <see cref="JsonCatalogStore"/>/<see cref="JsonPlaylistStore"/>: a tolerant load
/// (missing / unreadable / incompatible-version → <see cref="AppSettings.Default"/> + warning, never a
/// throw) and an atomic temp-then-move save so an interrupted write never corrupts the settings
/// (global standards #16/#26, #20/#22). Loaded settings are normalized so an out-of-range buffer or a
/// blank device name can never reach the audio device.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string>? _onWarning;

    public JsonSettingsStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _path = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "settings.json");
        _onWarning = onWarning;
    }

    /// <summary>The full path of the settings file.</summary>
    public string FilePath => _path;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return AppSettings.Default;

        SettingsSnapshot? snapshot;
        try
        {
            // ConfigureAwait(false) on the stream dispose as well as the deserialize: a caller that blocks
            // on this with GetResult() on a thread that has a SynchronizationContext (the UI thread, on
            // window close) would otherwise deadlock when the implicit DisposeAsync resumes on the blocked
            // context. See SaveAsync for the symptom this prevents.
            var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
            await using (stream.ConfigureAwait(false))
            {
                snapshot = await JsonSerializer
                    .DeserializeAsync<SettingsSnapshot>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Settings file at '{_path}' is unreadable ({ex.Message}); using defaults.");
            return AppSettings.Default;
        }

        if (snapshot is null)
            return AppSettings.Default;

        if (snapshot.Version is < 1 or > SettingsSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Settings file is version {snapshot.Version} (expected {SettingsSnapshot.CurrentVersion}); using defaults.");
            return AppSettings.Default;
        }

        return new AppSettings
        {
            Audio = new AudioSettings
            {
                OutputDeviceId = snapshot.OutputDeviceId,
                BufferMilliseconds = snapshot.BufferMilliseconds,
                CaptureDeviceId = snapshot.CaptureDeviceId,
                CaptureSource = snapshot.CaptureSource,
            },
            Midi = new MidiSettings
            {
                ControllerInputName = snapshot.MidiControllerInputName,
                FeedbackOutputName = snapshot.MidiFeedbackOutputName,
            },
            Extensions = new ExtensionSettings
            {
                DeveloperMode = snapshot.DeveloperMode,
                ActiveUiThemeId = snapshot.ActiveUiThemeId,
                ActiveKnobSkinId = snapshot.ActiveKnobSkinId,
                ActiveSliderSkinId = snapshot.ActiveSliderSkinId,
            },
            // Fields written before they existed read null → their defaults (back-compat, #20/#22).
            Visuals = new VisualsSettings(
                snapshot.WaveformZoomSeconds ?? VisualsSettings.DefaultZoomSeconds,
                snapshot.NudgeSeconds ?? VisualsSettings.DefaultNudgeSeconds),
            Diagnostics = new DiagnosticsSettings(
                snapshot.MinimumLogLevel ?? DiagnosticsSettings.DefaultMinimumLevel),
            Addons = new AddonSettings(
                snapshot.VuMeterBackgroundImagePath,
                (VuMeterNeedleOrigin)(snapshot.VuMeterNeedleOrigin ?? (int)VuMeterNeedleOrigin.Bottom)),
            // Fields written before they existed read null → their defaults (back-compat, #20/#22):
            // an older settings.json has no window layout, so the app opens full-screen on the first tab.
            WindowLayout = new WindowLayoutSettings(
                snapshot.ActiveTabId,
                snapshot.WindowWidth ?? WindowLayoutSettings.DefaultWidth,
                snapshot.WindowHeight ?? WindowLayoutSettings.DefaultHeight,
                snapshot.WindowX,
                snapshot.WindowY,
                snapshot.WindowIsFullScreen ?? true),
            // A file written before the terms field existed reads null -> 0 (not accepted), so the
            // first-launch acceptance gate prompts (back-compat, #20/#22).
            Legal = new LegalSettings(snapshot.AcceptedTermsVersion ?? 0),
            // Written before the update fields existed read null → check enabled, nothing skipped.
            Updates = new UpdateSettings(
                snapshot.CheckForUpdatesOnStartup ?? UpdateSettings.Default.CheckOnStartup,
                snapshot.SkippedUpdateVersion),
        }.Normalized();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings normalized = settings.Normalized();
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tempPath = _path + ".tmp";
        var snapshot = new SettingsSnapshot(
            SettingsSnapshot.CurrentVersion,
            normalized.Audio.OutputDeviceId,
            normalized.Audio.BufferMilliseconds,
            normalized.Midi.ControllerInputName,
            normalized.Midi.FeedbackOutputName,
            normalized.Audio.CaptureDeviceId,
            normalized.Audio.CaptureSource,
            normalized.Extensions.DeveloperMode,
            normalized.Extensions.ActiveUiThemeId,
            normalized.Visuals.WaveformZoomSeconds,
            normalized.Visuals.NudgeSeconds,
            normalized.Diagnostics.MinimumLevel,
            normalized.Addons.VuMeterBackgroundImagePath,
            ActiveKnobSkinId: normalized.Extensions.ActiveKnobSkinId,
            ActiveSliderSkinId: normalized.Extensions.ActiveSliderSkinId,
            ActiveTabId: normalized.WindowLayout.ActiveTabId,
            WindowWidth: normalized.WindowLayout.Width,
            WindowHeight: normalized.WindowLayout.Height,
            WindowX: normalized.WindowLayout.X,
            WindowY: normalized.WindowLayout.Y,
            WindowIsFullScreen: normalized.WindowLayout.IsFullScreen,
            AcceptedTermsVersion: normalized.Legal.AcceptedTermsVersion,
            CheckForUpdatesOnStartup: normalized.Updates.CheckOnStartup,
            SkippedUpdateVersion: normalized.Updates.SkippedVersion);

        // ConfigureAwait(false) on BOTH the serialize and the stream's implicit DisposeAsync. Without it,
        // a synchronous-completing SerializeAsync (small settings JSON) leaves the closing DisposeAsync to
        // resume on the captured SynchronizationContext — so SaveWindowLayout's GetResult() on the UI
        // thread at window close DEADLOCKS the app ("X freezes, only killing the process stops it").
        var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        File.Move(tempPath, _path, overwrite: true);
    }
}
