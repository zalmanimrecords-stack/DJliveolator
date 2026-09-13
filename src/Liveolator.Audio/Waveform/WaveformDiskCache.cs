using Liveolator.Core.Waveform;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Waveform;

/// <summary>Small binary, regenerable overview cache. At most 128 files / 256 MiB, never audio copies.</summary>
internal sealed class WaveformDiskCache(string directory, ILogger logger)
{
    private const int Magic = 0x4C575631;
    private const long MaxBytes = 256L * 1024 * 1024;
    private const int MaxFiles = 128;
    private const int MaxPeaks = DecodedWaveformProvider.MaxBuckets;

    public WaveformOverview? Read(string key)
    {
        string path = Path.Combine(directory, key + ".wave");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            if (stream.Length > 4L * MaxPeaks * sizeof(float) + 64) return null;
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic) return null;
            double duration = reader.ReadDouble();
            if (!double.IsFinite(duration) || duration < 0) return null;
            float[] peaks = ReadBand(reader);
            if (peaks.Length == 0) return null;
            float[] low = ReadBand(reader), mid = ReadBand(reader), high = ReadBand(reader);
            if (stream.Position != stream.Length ||
                !Aligned(low, peaks) || !Aligned(mid, peaks) || !Aligned(high, peaks)) return null;
            return new WaveformOverview(peaks, duration,
                low.Length == 0 ? null : low, mid.Length == 0 ? null : mid, high.Length == 0 ? null : high);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogDebug(ex, "Waveform cache read failed; rebuilding {CacheKey}.", key);
            return null;
        }
    }

    public void Write(string key, WaveformOverview overview)
    {
        string path = Path.Combine(directory, key + ".wave");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(overview.DurationSeconds);
                WriteBand(writer, overview.Peaks);
                WriteBand(writer, overview.LowPeaks);
                WriteBand(writer, overview.MidPeaks);
                WriteBand(writer, overview.HighPeaks);
            }
            File.Move(temporary, path, overwrite: true);
            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogDebug(ex, "Waveform cache write failed; decoded overview remains available.");
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { logger.LogDebug(ex, "Could not remove incomplete waveform cache entry."); }
        }
    }

    private static float[] ReadBand(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxPeaks || reader.BaseStream.Length - reader.BaseStream.Position < (long)count * sizeof(float))
            throw new InvalidDataException("Invalid waveform band length.");
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            float value = reader.ReadSingle();
            if (!float.IsFinite(value) || value < 0 || value > 1) throw new InvalidDataException("Invalid waveform peak.");
            values[i] = value;
        }
        return values;
    }

    private static bool Aligned(float[] band, float[] peaks) => band.Length == 0 || band.Length == peaks.Length;

    private static void WriteBand(BinaryWriter writer, IReadOnlyList<float>? values)
    {
        writer.Write(values?.Count ?? 0);
        if (values is not null)
            for (int i = 0; i < values.Count; i++) writer.Write(values[i]);
    }

    private void Prune()
    {
        var files = new DirectoryInfo(directory).GetFiles("*.wave").OrderByDescending(file => file.LastWriteTimeUtc);
        long bytes = 0;
        int count = 0;
        foreach (var file in files)
        {
            bytes += file.Length;
            if (++count > MaxFiles || bytes > MaxBytes) file.Delete();
        }
    }
}
