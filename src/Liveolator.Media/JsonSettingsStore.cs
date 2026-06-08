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
    double? NudgeSeconds = null)
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
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
            snapshot = await JsonSerializer
                .DeserializeAsync<SettingsSnapshot>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
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
            },
            // Fields written before they existed read null → their defaults (back-compat, #20/#22).
            Visuals = new VisualsSettings(
                snapshot.WaveformZoomSeconds ?? VisualsSettings.DefaultZoomSeconds,
                snapshot.NudgeSeconds ?? VisualsSettings.DefaultNudgeSeconds),
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
            normalized.Visuals.NudgeSeconds);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }
}
