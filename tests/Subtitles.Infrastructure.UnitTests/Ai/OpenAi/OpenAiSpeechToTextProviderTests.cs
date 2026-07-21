using System.Net;
using Microsoft.Extensions.Options;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.UnitTests.TestSupport;
using Xunit;

namespace Subtitles.Infrastructure.UnitTests.Ai.OpenAi;

public class OpenAiSpeechToTextProviderTests
{
    private static OpenAiSpeechToTextProvider CreateProvider(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var options = Options.Create(new OpenAiSttOptions { ApiKey = "test-key", Model = "whisper-1" });
        return new OpenAiSpeechToTextProvider(httpClient, options);
    }

    [Fact]
    public async Task TranscribeAsync_WithValidResponse_ParsesTextLanguageAndWordTimestamps()
    {
        const string fixture = """
            {
              "task": "transcribe",
              "language": "telugu",
              "duration": 3.2,
              "text": "Eeroju manam matladukundham",
              "words": [
                { "word": "Eeroju", "start": 0.0, "end": 0.42 },
                { "word": "manam", "start": 0.45, "end": 0.8 },
                { "word": "matladukundham", "start": 0.85, "end": 1.6 }
              ]
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), CancellationToken.None);

        Assert.Equal("Eeroju manam matladukundham", result.Text);
        Assert.Equal("telugu", result.LanguageCode);
        Assert.Equal(3, result.Words.Count);
        Assert.Equal("Eeroju", result.Words[0].Text);
        Assert.Equal(0, result.Words[0].StartMs);
        Assert.Equal(420, result.Words[0].EndMs);
        Assert.Equal(850, result.Words[2].StartMs);
    }

    [Fact]
    public async Task TranscribeAsync_SendsModelAndWordTimestampGranularity()
    {
        const string fixture = """{"text":"x","language":"english","words":[]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), CancellationToken.None);

        Assert.Contains("whisper-1", handler.LastRequestBody);
        Assert.Contains("verbose_json", handler.LastRequestBody);
        Assert.Contains("word", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranscribeAsync_WithHttpErrorStatus_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"invalid file format"}}"""),
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranscribeAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));

        Assert.Contains("invalid file format", ex.Message);
    }
}
