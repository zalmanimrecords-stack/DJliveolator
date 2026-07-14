using System.Net;
using System.Threading.Tasks;
using Liveolator.Online;
using Xunit;

namespace Liveolator.Online.Tests;

public class HttpUpdateManifestSourceTests
{
    private const string ManifestUrl = "https://example.test/version.json";

    private const string ValidManifest = """
    {
      "version": "0.1.5",
      "downloadUrl": "https://example.test/downloads/LiveolatorSetup-0.1.5.exe",
      "notes": [ "First note", "Second note" ]
    }
    """;

    private static HttpUpdateManifestSource Source(FakeHttpMessageHandler handler)
        => new(handler.ToClient(), ManifestUrl);

    [Fact]
    public async Task Fetch_ParsesVersionDownloadUrlAndNotes()
    {
        var result = await Source(new FakeHttpMessageHandler(ValidManifest)).FetchAsync();

        Assert.NotNull(result);
        Assert.Equal("0.1.5", result!.Version);
        Assert.Equal("https://example.test/downloads/LiveolatorSetup-0.1.5.exe", result.DownloadUrl);
        Assert.Equal(new[] { "First note", "Second note" }, result.Notes);
    }

    [Fact]
    public async Task Fetch_RequestsTheConfiguredUrl()
    {
        var handler = new FakeHttpMessageHandler(ValidManifest);
        await Source(handler).FetchAsync();

        Assert.Equal(ManifestUrl, handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task Fetch_MissingNotes_YieldsEmptyNotes()
    {
        const string body = """{ "version": "0.1.5", "downloadUrl": "https://example.test/x.exe" }""";

        var result = await Source(new FakeHttpMessageHandler(body)).FetchAsync();

        Assert.NotNull(result);
        Assert.Empty(result!.Notes);
    }

    [Theory]
    [InlineData("""{ "downloadUrl": "https://example.test/x.exe" }""")] // no version
    [InlineData("""{ "version": "0.1.5" }""")]                          // no downloadUrl
    [InlineData("""{ "version": "  ", "downloadUrl": "https://example.test/x.exe" }""")] // blank version
    public async Task Fetch_MissingRequiredFields_ReturnsNull(string body)
        => Assert.Null(await Source(new FakeHttpMessageHandler(body)).FetchAsync());

    [Fact]
    public async Task Fetch_NonSuccessStatus_ReturnsNull()
        => Assert.Null(await Source(new FakeHttpMessageHandler("nope", HttpStatusCode.NotFound)).FetchAsync());

    [Fact]
    public async Task Fetch_MalformedJson_ReturnsNull()
        => Assert.Null(await Source(new FakeHttpMessageHandler("{ this is not json")).FetchAsync());
}
