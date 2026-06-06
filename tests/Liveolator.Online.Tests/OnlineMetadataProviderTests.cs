using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Enrichment;
using Liveolator.Online;
using Xunit;

namespace Liveolator.Online.Tests;

public class OnlineMetadataProviderTests
{
    private sealed class FakeAcoustId : IAcoustIdClient
    {
        public RecordingMatch? Result { get; set; }
        public int Calls { get; private set; }
        public Task<RecordingMatch?> LookupAsync(string fingerprint, int durationSeconds, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeBpm : IGetSongBpmClient
    {
        public string? LastArtist { get; private set; }
        public string? LastTitle { get; private set; }
        public int Calls { get; private set; }
        public OnlineTrackMetadata? Result { get; set; } = new(140, null, "Am", "psytrance", "GetSongBPM");

        public Task<OnlineTrackMetadata?> SearchAsync(string artist, string title, CancellationToken ct = default)
        {
            Calls++;
            LastArtist = artist;
            LastTitle = title;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task FingerprintMatch_DrivesTheBpmLookup()
    {
        var acoustId = new FakeAcoustId { Result = new RecordingMatch("Loud", "5 Billion Stars", 0.9) };
        var bpm = new FakeBpm();
        var provider = new OnlineMetadataProvider(bpm, acoustId);

        OnlineTrackMetadata? result = await provider.LookupAsync(
            new TrackQuery(AcoustId: "FP", Duration: TimeSpan.FromMinutes(9)));

        Assert.Equal(1, acoustId.Calls);
        Assert.Equal("Loud", bpm.LastArtist);          // identity from the fingerprint drove the search
        Assert.Equal("5 Billion Stars", bpm.LastTitle);
        Assert.Equal(140, result!.Bpm);
    }

    [Fact]
    public async Task NoFingerprint_FallsBackToTags()
    {
        var acoustId = new FakeAcoustId();
        var bpm = new FakeBpm();
        var provider = new OnlineMetadataProvider(bpm, acoustId);

        await provider.LookupAsync(new TrackQuery(Artist: "Loud", Title: "5 Billion Stars"));

        Assert.Equal(0, acoustId.Calls);               // no fingerprint → don't call AcoustID
        Assert.Equal("Loud", bpm.LastArtist);
    }

    [Fact]
    public async Task FingerprintMiss_FallsBackToTagsWhenPresent()
    {
        var acoustId = new FakeAcoustId { Result = null }; // no match
        var bpm = new FakeBpm();
        var provider = new OnlineMetadataProvider(bpm, acoustId);

        await provider.LookupAsync(new TrackQuery(
            Artist: "Loud", Title: "5 Billion Stars", AcoustId: "FP", Duration: TimeSpan.FromMinutes(9)));

        Assert.Equal(1, acoustId.Calls);
        Assert.Equal("Loud", bpm.LastArtist);          // fell back to the tags
    }

    [Fact]
    public async Task NoTagsAndNoFingerprintMatch_ReturnsNull_WithoutBpmCall()
    {
        var acoustId = new FakeAcoustId { Result = null };
        var bpm = new FakeBpm();
        var provider = new OnlineMetadataProvider(bpm, acoustId);

        OnlineTrackMetadata? result = await provider.LookupAsync(
            new TrackQuery(AcoustId: "FP", Duration: TimeSpan.FromMinutes(9)));

        Assert.Null(result);
        Assert.Equal(0, bpm.Calls);                    // nothing to search with
    }

    [Fact]
    public async Task WorksWithoutAnAcoustIdClient_TagsOnly()
    {
        var bpm = new FakeBpm();
        var provider = new OnlineMetadataProvider(bpm); // no fingerprint client wired

        OnlineTrackMetadata? result = await provider.LookupAsync(new TrackQuery(Artist: "Loud", Title: "Track"));

        Assert.Equal(140, result!.Bpm);
        Assert.Equal(1, bpm.Calls);
    }
}
