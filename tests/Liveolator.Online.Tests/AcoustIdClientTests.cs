using System.Net;
using System.Threading.Tasks;
using Liveolator.Online;
using Xunit;

namespace Liveolator.Online.Tests;

public class AcoustIdClientTests
{
    private const string TwoResults = """
    {
      "status": "ok",
      "results": [
        { "score": 0.55, "recordings": [ { "id": "r1", "title": "Low Score", "artists": [ { "name": "Someone" } ] } ] },
        { "score": 0.93, "recordings": [ { "id": "r2", "title": "5 Billion Stars", "artists": [ { "name": "Loud" }, { "name": "Captain Hook" } ] } ] }
      ]
    }
    """;

    [Fact]
    public async Task Lookup_PicksHighestScoringMatch_AndJoinsArtists()
    {
        var handler = new FakeHttpMessageHandler(TwoResults);
        var client = new AcoustIdClient(handler.ToClient(), clientKey: "k");

        RecordingMatch? match = await client.LookupAsync("FINGERPRINT", durationSeconds: 570);

        Assert.NotNull(match);
        Assert.Equal("5 Billion Stars", match!.Title);
        Assert.Equal("Loud, Captain Hook", match.Artist);
        Assert.Equal(0.93, match.Score, 6);
    }

    [Fact]
    public async Task Lookup_SendsClientKeyDurationAndFingerprint()
    {
        var handler = new FakeHttpMessageHandler(TwoResults);
        var client = new AcoustIdClient(handler.ToClient(), clientKey: "mykey");

        await client.LookupAsync("FP123", durationSeconds: 570);

        string url = handler.LastRequestUri!.ToString();
        Assert.Contains("client=mykey", url);
        Assert.Contains("duration=570", url);
        Assert.Contains("fingerprint=FP123", url);
        Assert.Contains("meta=recordings", url);
    }

    [Fact]
    public async Task Lookup_EmptyResults_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler("""{ "status": "ok", "results": [] }""");
        var client = new AcoustIdClient(handler.ToClient(), clientKey: "k");

        Assert.Null(await client.LookupAsync("FP", 200));
    }

    [Fact]
    public async Task Lookup_NonSuccess_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler("error", HttpStatusCode.ServiceUnavailable);
        var client = new AcoustIdClient(handler.ToClient(), clientKey: "k");

        Assert.Null(await client.LookupAsync("FP", 200));
    }

    [Fact]
    public async Task Lookup_BlankFingerprint_ReturnsNull_WithoutCallingHttp()
    {
        var handler = new FakeHttpMessageHandler(TwoResults);
        var client = new AcoustIdClient(handler.ToClient(), clientKey: "k");

        Assert.Null(await client.LookupAsync("  ", 200));
        Assert.Null(handler.LastRequestUri); // never hit the network
    }
}
