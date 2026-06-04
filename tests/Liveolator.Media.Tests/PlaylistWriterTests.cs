using System.Text.Json;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Media.Tests;

public class PlaylistWriterTests
{
    private readonly PlaylistWriter _writer = new();

    private static IReadOnlyList<MusicTrack> TwoTracks() => new[]
    {
        TestTracks.Analyzed("a.wav", 124, tonic: 0, mode: KeyMode.Major),
        TestTracks.Analyzed("b.wav", 126, tonic: 7, mode: KeyMode.Minor),
    };

    [Fact]
    public async Task Write_M3U8_HasExtmHeaderAndOneExtinfPerTrack()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "set.m3u8");

        await _writer.WriteAsync(TwoTracks(), path, PlaylistFormat.M3U8);

        string content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("#EXTM3U", content);
        Assert.Equal(2, content.Split("#EXTINF:").Length - 1);
        Assert.Contains("a.wav", content);
        Assert.Contains("b.wav", content);
    }

    [Fact]
    public async Task Write_Json_IsParseableWithAnalysisFields()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "set.json");

        await _writer.WriteAsync(TwoTracks(), path, PlaylistFormat.Json);

        using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal(124.0, root[0].GetProperty("Bpm").GetDouble());
        Assert.Equal("8B", root[0].GetProperty("Camelot").GetString());
    }

    [Fact]
    public async Task Write_CreatesMissingDirectory()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "nested", "deep", "set.m3u8");

        await _writer.WriteAsync(TwoTracks(), path, PlaylistFormat.M3U8);

        Assert.True(File.Exists(path));
    }
}
