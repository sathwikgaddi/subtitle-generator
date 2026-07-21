namespace Subtitles.Infrastructure.UnitTests.TestSupport;

/// <summary>
/// Returns a canned HttpResponseMessage instead of making a real network call — this is how
/// the provider unit tests verify our request/response parsing without spending money on
/// real OpenAI calls (see the LiveOpenAI-tagged tests for the real-call verification).
/// </summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return respond(request);
    }
}
