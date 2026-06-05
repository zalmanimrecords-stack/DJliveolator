using ATL;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Audio.Tests;

public class AtlMetadataReaderTests
{
    private readonly AtlMetadataReader _reader = new();

    [Fact]
    public void Read_RoundTripsTagsAndStreamInfo()
    {
        // A valid WAV (2 ch / 44.1k) carries a LIST-INFO tag chunk that ATL round-trips.
        float[] stereo = new float[44100 * 2]; // ~0.5s of silence, both channels
        string path = WavTestFile.WriteFloat32(stereo, channels: 2, sampleRate: 44100);
        try
        {
            var tagged = new Track(path)
            {
                Title = "Real Title",
                Artist = "M83",
                Album = "Hurry Up, We're Dreaming",
                Genre = "Electronic",
                Year = 2011,
                TrackNumber = 3,
                Comment = "demo",
            };
            tagged.Save();

            TrackMetadata? meta = _reader.Read(path);

            Assert.NotNull(meta);
            Assert.Equal("Real Title", meta!.Title);
            Assert.Equal("M83", meta.Artist);
            Assert.Equal("Hurry Up, We're Dreaming", meta.Album);
            Assert.Equal("Electronic", meta.Genre);
            Assert.Equal(2011, meta.Year);
            Assert.Equal(3, meta.TrackNumber);
            // Stream facts come from the decoded header, not the tags.
            Assert.Equal(44100, meta.SampleRateHz);
            Assert.Equal(2, meta.Channels);
            Assert.False(string.IsNullOrWhiteSpace(meta.Codec));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_UntaggedFile_ReturnsStreamInfoWithNullTags()
    {
        float[] mono = new float[22050];
        string path = WavTestFile.WritePcm16(mono, channels: 1, sampleRate: 22050);
        try
        {
            TrackMetadata? meta = _reader.Read(path);

            Assert.NotNull(meta);
            Assert.Null(meta!.Title);   // no tags written → null, not ""
            Assert.Null(meta.Artist);
            Assert.Equal(22050, meta.SampleRateHz);
            Assert.Equal(1, meta.Channels);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_NonAudioFile_DoesNotThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-{Guid.NewGuid():N}.mp3");
        File.WriteAllText(path, "this is not audio");
        try
        {
            // Contract: a garbage file must never throw — at worst it yields null/empty metadata.
            TrackMetadata? meta = _reader.Read(path);
            if (meta is not null)
                Assert.Null(meta.Artist);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_MissingPath_ReturnsNull()
        => Assert.Null(_reader.Read("   "));
}
