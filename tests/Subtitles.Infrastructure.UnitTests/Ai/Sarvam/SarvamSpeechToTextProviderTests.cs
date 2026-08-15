using System.Net;
using Microsoft.Extensions.Options;
using Subtitles.Infrastructure.Ai.Sarvam;
using Subtitles.Infrastructure.UnitTests.TestSupport;
using Xunit;

namespace Subtitles.Infrastructure.UnitTests.Ai.Sarvam;

public class SarvamSpeechToTextProviderTests
{
    private static SarvamSpeechToTextProvider CreateProvider(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.sarvam.ai/") };
        var options = Options.Create(new SarvamSttOptions { ApiKey = "test-key", Model = "saaras:v4" });
        return new SarvamSpeechToTextProvider(httpClient, options);
    }

    [Fact]
    public async Task TranscribeAsync_WithValidResponse_ParsesTranscriptAndNormalizesLanguageCode()
    {
        const string fixture = """
            {
              "request_id": "abc",
              "transcript": "Eeroju manam matladukundham",
              "language_code": "te-IN",
              "timestamps": {
                "words": ["Eeroju manam", "matladukundham"],
                "start_time_seconds": [0.0, 1.2],
                "end_time_seconds": [1.0, 2.5]
              },
              "language_probability": null
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Equal("Eeroju manam matladukundham", result.Text);
        // "te-IN" -> "te", stripped to match the bare codes the rest of this app uses.
        Assert.Equal("te", result.LanguageCode);
    }

    [Fact]
    public async Task TranscribeAsync_SplitsMultiWordPhrasesIntoIndividualApproximatelyTimedWords()
    {
        // Sarvam's own "words" field is phrase-level despite the name (confirmed against their
        // API reference docs) — a 2-word phrase spanning 1000ms should become 2 Word entries
        // each covering half that span, not one Word containing both words.
        const string fixture = """
            {
              "transcript": "Eeroju manam",
              "language_code": "te-IN",
              "timestamps": {
                "words": ["Eeroju manam"],
                "start_time_seconds": [0.0],
                "end_time_seconds": [1.0]
              }
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Equal(2, result.Words.Count);
        Assert.Equal("Eeroju", result.Words[0].Text);
        Assert.Equal(0, result.Words[0].StartMs);
        Assert.Equal(500, result.Words[0].EndMs);
        Assert.Equal("manam", result.Words[1].Text);
        Assert.Equal(500, result.Words[1].StartMs);
        Assert.Equal(1000, result.Words[1].EndMs);
    }

    [Fact]
    public async Task TranscribeAsync_WithLanguageHint_SendsBcp47CodeAndAuthHeader()
    {
        const string fixture = """{"transcript":"x","language_code":"te-IN","timestamps":{"words":[],"start_time_seconds":[],"end_time_seconds":[]}}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", "te", CancellationToken.None);

        Assert.Contains("te-IN", handler.LastRequestBody);
        Assert.Equal("test-key", handler.LastRequest!.Headers.GetValues("api-subscription-key").Single());
    }

    [Fact]
    public async Task TranscribeAsync_WithoutLanguageHint_SendsUnknownForAutoDetect()
    {
        const string fixture = """{"transcript":"x","language_code":"te-IN","timestamps":{"words":[],"start_time_seconds":[],"end_time_seconds":[]}}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Contains("unknown", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranscribeAsync_WithHttpErrorStatus_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid file"}"""),
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None));

        Assert.Contains("invalid file", ex.Message);
    }
}
