using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals.TrackPrograms;

namespace Liveolator.Media;

/// <summary>Versioned on-disk envelope for one authored track visual program.</summary>
public sealed record TrackVisualProgramSnapshot(int Version, TrackVisualProgram? Program)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Stores one authored visual program per track under <c>live/track-visuals/</c>. File names are
/// stable SHA-256 hashes of normalized track paths, keeping user paths out of file names.
/// </summary>
public sealed class JsonTrackVisualProgramStore : ITrackVisualProgramStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Action<string>? _onWarning;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonTrackVisualProgramStore(
        string? rootDirectory = null,
        Action<string>? onWarning = null)
    {
        ProgramDirectory = Path.Combine(
            rootDirectory ?? JsonCatalogStore.DefaultRoot(),
            "live",
            "track-visuals");
        _onWarning = onWarning;
    }

    public string ProgramDirectory { get; }

    public string PathFor(string trackPath)
    {
        string normalized = NormalizeTrackPath(trackPath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Path.Combine(ProgramDirectory, Convert.ToHexString(hash).ToLowerInvariant() + ".json");
    }

    public async Task<TrackVisualProgram?> LoadAsync(
        string trackPath,
        CancellationToken cancellationToken = default)
    {
        string path = PathFor(trackPath);
        TrackVisualProgramSnapshot? snapshot = await LoadSnapshotAsync(path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != TrackVisualProgramSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Track visual program at '{path}' is version {snapshot.Version} " +
                $"(expected {TrackVisualProgramSnapshot.CurrentVersion}); ignoring it.");
            return null;
        }

        if (snapshot.Program is null)
        {
            _onWarning?.Invoke($"Track visual program at '{path}' contains no program; ignoring it.");
            return null;
        }

        if (!PathEquals(snapshot.Program.Track.Path, trackPath))
        {
            _onWarning?.Invoke(
                $"Track visual program at '{path}' belongs to a different track; ignoring it.");
            return null;
        }

        return snapshot.Program;
    }

    public async Task SaveAsync(
        TrackVisualProgram program,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        string path = PathFor(program.Track.Path);
        var snapshot = new TrackVisualProgramSnapshot(
            TrackVisualProgramSnapshot.CurrentVersion,
            program);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ProgramDirectory);
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(
                        stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
                }

                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string trackPath,
        CancellationToken cancellationToken = default)
    {
        string path = PathFor(trackPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TrackVisualProgramSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ProgramDirectory))
            return Array.Empty<TrackVisualProgramSummary>();

        string[] paths;
        try
        {
            paths = Directory.GetFiles(ProgramDirectory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke(
                $"Track visual program directory '{ProgramDirectory}' is unreadable ({ex.Message}); ignoring it.");
            return Array.Empty<TrackVisualProgramSummary>();
        }

        var summaries = new List<TrackVisualProgramSummary>();
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrackVisualProgramSnapshot? snapshot =
                await LoadSnapshotAsync(path, cancellationToken).ConfigureAwait(false);
            if (snapshot?.Version != TrackVisualProgramSnapshot.CurrentVersion ||
                snapshot.Program is not { } program)
            {
                continue;
            }

            summaries.Add(new TrackVisualProgramSummary(
                program.Id,
                program.Track.Path,
                program.Cues.Count,
                program.Fallback));
        }

        return summaries
            .OrderBy(summary => summary.TrackPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<TrackVisualProgramSnapshot?> LoadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<TrackVisualProgramSnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _onWarning?.Invoke(
                $"Track visual program at '{path}' is unreadable ({ex.Message}); ignoring it.");
            return null;
        }
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(
            NormalizeTrackPath(left),
            NormalizeTrackPath(right),
            StringComparison.Ordinal);

    private static string NormalizeTrackPath(string trackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);
        string normalized = Path.GetFullPath(trackPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }
}
