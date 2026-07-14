using System.Net;

namespace Liveolator.Online.Tests;

/// <summary>
/// Test <see cref="HttpMessageHandler"/> that returns a canned response and records the requested URI,
/// so the HTTP clients parse real JSON without any network. Construct an <see cref="HttpClient"/> with
/// a base address around it.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    /// <summary>The absolute URI of the most recent request (for asserting query construction).</summary>
    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body),
            RequestMessage = request,
        });
    }

    /// <summary>Builds an HttpClient pointed at a dummy base address through this handler.</summary>
    public HttpClient ToClient(string baseAddress = "https://example.test/")
        => new(this) { BaseAddress = new Uri(baseAddress) };
}
