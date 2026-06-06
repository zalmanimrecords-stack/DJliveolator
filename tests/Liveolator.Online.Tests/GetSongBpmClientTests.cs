using System.Net;
using System.Threading.Tasks;
using Liveolator.Online;
using Xunit;

namespace Liveolator.Online.Tests;

public class GetSongBpmClientTests
{
    private const string Hit = """
    {
      "search": [
        {
          "id": "abc",
          "title": "5 Billion Stars (Captain Hook Remix)",
          "tempo": "140",
          "key_of": "Am",
          "artist": { "name": "Loud", "genres": [ "psytrance", "trance" ] }
        }
      ]
    }
    """;

    [Fact]
    public async Task Search_ParsesTempoKeyAndGenre()
    {
        var handler = new FakeHttpMessageHandler(Hit);
        var client = new GetSongBpmClient(handler.ToClient(), apiKey: "k");

        var result = await client.SearchAsync("Loud", "5 Billion Stars (Captain Hook Remix)");

        Assert.NotNull(result);
        Assert.Equal(140.0, result!.Bpm);
        Assert.Equal("Am", result.KeyName);
        Assert.Equal("psytrance", result.Genre);
        Assert.Equal(GetSongBpmClient.SourceName, result.Source);
    }

    [Fact]
    public async Task Search_SendsApiKeyAndLookup()
    {
        var handler = new FakeHttpMessageHandler(Hit);
        var client = new GetSongBpmClient(handler.ToClient(), apiKey: "mykey");

        await client.SearchAsync("Loud", "5 Billion Stars");

        string url = Uri.UnescapeDataString(handler.LastRequestUri!.ToString());
        Assert.Contains("api_key=mykey", url);
        Assert.Contains("song:5 Billion Stars", url);
        Assert.Contains("artist:Loud", url);
    }

    [Fact]
    public async Task Search_NonSuccess_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler("nope", HttpStatusCode.TooManyRequests);
        var client = new GetSongBpmClient(handler.ToClient(), apiKey: "k");

        Assert.Null(await client.SearchAsync("Loud", "5 Billion Stars"));
    }

    [Fact]
    public async Task Search_EmptyResults_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler("""{ "search": [] }""");
        var client = new GetSongBpmClient(handler.ToClient(), apiKey: "k");

        Assert.Null(await client.SearchAsync("Loud", "5 Billion Stars"));
    }

    [Fact]
    public async Task Search_BlankInput_ReturnsNull_WithoutCallingHttp()
    {
        var handler = new FakeHttpMessageHandler(Hit);
        var client = new GetSongBpmClient(handler.ToClient(), apiKey: "k");

        Assert.Null(await client.SearchAsync("", "title"));
        Assert.Null(handler.LastRequestUri);
    }
}
