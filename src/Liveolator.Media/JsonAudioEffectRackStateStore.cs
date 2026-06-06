using System.Text.Json;
using Liveolator.Core.Audio.Effects;

namespace Liveolator.Media;

internal sealed record AudioEffectRacksSnapshot(
    int Version,
    IReadOnlyList<AudioEffectRackState> Racks)
{
    public const int CurrentVersion = 1;
}

public sealed class JsonAudioEffectRackStateStore : IAudioEffectRackStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Action<string>? _onWarning;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public JsonAudioEffectRackStateStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _path = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", "audio-fx-racks.json");
        _onWarning = onWarning;
    }

    public string FilePath => _path;

    public async Task<IReadOnlyList<AudioEffectRackState>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return Array.Empty<AudioEffectRackState>();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            AudioEffectRacksSnapshot? snapshot = await JsonSerializer.DeserializeAsync<AudioEffectRacksSnapshot>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (snapshot?.Version != AudioEffectRacksSnapshot.CurrentVersion)
            {
                _onWarning?.Invoke("Audio effect rack state has an unsupported version; ignoring it.");
                return Array.Empty<AudioEffectRackState>();
            }
            return snapshot.Racks
                .Where(r => r.Slot >= 0 && r.Slot < AudioEffectRackSlot.Count)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Audio effect rack state is unreadable ({ex.Message}); using empty racks.");
            return Array.Empty<AudioEffectRackState>();
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<AudioEffectRackState> racks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(racks);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            var snapshot = new AudioEffectRacksSnapshot(
                AudioEffectRacksSnapshot.CurrentVersion,
                racks.OrderBy(r => r.Slot).ToArray());
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
