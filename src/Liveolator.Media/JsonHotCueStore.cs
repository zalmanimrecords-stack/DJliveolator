using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted per-track cue store (doc 11/13).</summary>
public sealed record HotCueSnapshot(int Version, IReadOnlyList<TrackCueRecord> Tracks)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists per-track hot/primary cues as a single JSON file under the per-user app-data root, keyed
/// by track path, so a DJ's cues reload on the next run (doc 11/13). Held separately from the music
/// catalog so cue edits never invalidate the analyzed catalog and existing catalog files stay valid
/// (backward-compatible by construction — global standards #20/#22).
/// </summary>
/// <remarks>
/// A missing or corrupt file yields no cues (clean slate) and reports a warning — it never crashes the
/// app (global standards #16/#26). Saves are atomic (temp-then-move) so an interrupted write never
/// corrupts the live cue file.
/// </remarks>
public sealed class JsonHotCueStore : IHotCueStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;
    private readonly JsonFileSnapshotIo _io;

    // Serializes read-modify-write so two concurrent SaveAsync calls cannot clobber each other's edits.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonHotCueStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = rootDirectory ?? DefaultRoot();
        _onWarning = onWarning;
        _io = new JsonFileSnapshotIo(onWarning);
    }

    /// <summary>Full path of the per-track cue JSON file.</summary>
    public string CuesPath => Path.Combine(_directory, "catalog.cues.json");

    /// <summary>Default persistence root: <c>%APPDATA%/Liveolator</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator");

    public async Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);

        IReadOnlyDictionary<string, TrackCueRecord> all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.TryGetValue(trackPath, out TrackCueRecord? record) ? record : null;
    }

    public async Task SaveAsync(TrackCueRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.TrackPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, TrackCueRecord> all = new(await LoadAllAsync(cancellationToken).ConfigureAwait(false));
            all[record.TrackPath] = record;
            await PersistAsync(all.Values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, TrackCueRecord> all = new(await LoadAllAsync(cancellationToken).ConfigureAwait(false));
            if (all.Remove(trackPath))
                await PersistAsync(all.Values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, TrackCueRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CuesPath))
            return EmptyMap();

        HotCueSnapshot? snapshot;
        try
        {
            await using var stream = new FileStream(CuesPath, FileMode.Open, FileAccess.Read);
            snapshot = await JsonSerializer.DeserializeAsync<HotCueSnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Cue store at '{CuesPath}' is unreadable ({ex.Message}); starting with no saved cues.");
            return EmptyMap();
        }

        if (snapshot is null)
            return EmptyMap();

        if (snapshot.Version != HotCueSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Cue store at '{CuesPath}' is version {snapshot.Version} " +
                $"(expected {HotCueSnapshot.CurrentVersion}); ignoring.");
            return EmptyMap();
        }

        var map = new Dictionary<string, TrackCueRecord>();
        foreach (TrackCueRecord record in snapshot.Tracks ?? Array.Empty<TrackCueRecord>())
        {
            if (!string.IsNullOrWhiteSpace(record.TrackPath))
                map[record.TrackPath] = record;
        }

        return map;
    }

    private async Task PersistAsync(IEnumerable<TrackCueRecord> records, CancellationToken cancellationToken)
    {
        var snapshot = new HotCueSnapshot(HotCueSnapshot.CurrentVersion, records.ToList());
        // Atomic temp-then-move via the shared helper (unique temp name + orphaned-temp cleanup), so an
        // interrupted write never corrupts the live cue file.
        await _io.SaveAsync(CuesPath, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, TrackCueRecord> EmptyMap() => new();
}
