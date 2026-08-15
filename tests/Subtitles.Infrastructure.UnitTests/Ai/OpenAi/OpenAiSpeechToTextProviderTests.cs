using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
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
        return new OpenAiSpeechToTextProvider(httpClient, options, NullLogger<OpenAiSpeechToTextProvider>.Instance);
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

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

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

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Contains("whisper-1", handler.LastRequestBody);
        Assert.Contains("verbose_json", handler.LastRequestBody);
        Assert.Contains("word", handler.LastRequestBody);
        // Locks in the fix: Whisper determines audio format from the filename's extension,
        // so the multipart part must carry a real extension, not a bare "audio".
        Assert.Contains("audio.mp3", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranscribeAsync_WithNullLanguage_ReturnsZeroConfidenceAndEmptyCode()
    {
        // Confirmed against a real OpenAI response (see OpenAiSpeechToTextProviderLiveTests):
        // "language": null is what a genuinely no-speech/undetectable clip actually returns —
        // there is no separate numeric confidence field on this endpoint at all.
        const string fixture = """{"task":"transcribe","language":null,"duration":3.0,"text":"","words":[]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Equal(string.Empty, result.LanguageCode);
        Assert.Equal(0.0, result.LanguageConfidence);
    }

    [Fact]
    public async Task TranscribeAsync_WithDetectedLanguage_ReturnsFullConfidence()
    {
        const string fixture = """{"task":"transcribe","language":"english","duration":3.0,"text":"hi","words":[]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.Equal("english", result.LanguageCode);
        Assert.Equal(1.0, result.LanguageConfidence);
    }

    [Fact]
    public async Task TranscribeAsync_WhenLanguageHintIsRejected_FallsBackToAutoDetect()
    {
        // Confirmed live: OpenAI's hosted whisper-1 rejects some languages Whisper can
        // perfectly well auto-detect (e.g. Telugu) when forced via the "language" parameter —
        // this locks in the fallback so a rejected hint degrades to auto-detect instead of
        // failing the whole transcription.
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"error":{"message":"Language 'te' is not supported.","type":"invalid_request_error","param":"language","code":"unsupported_language"}}"""),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"task":"transcribe","language":"telugu","duration":3.0,"text":"hi","words":[]}"""),
            };
        });
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", "te", CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal("telugu", result.LanguageCode);
        Assert.DoesNotContain("name=language", handler.LastRequestBody); // the retry omitted the hint
    }

    [Fact]
    public async Task TranscribeAsync_WithUnrelatedError_DoesNotRetry()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"invalid file format"}}"""),
            };
        });
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", "te", CancellationToken.None));

        Assert.Equal(1, callCount); // no retry for an error unrelated to the language hint
    }

    [Fact]
    public async Task TranscribeAsync_WithLanguageHint_IncludesLanguageFieldInRequest()
    {
        const string fixture = """{"text":"x","language":"telugu","words":[]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", "te", CancellationToken.None);

        Assert.Contains("name=language", handler.LastRequestBody);
        Assert.Contains("\r\n\r\nte", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranscribeAsync_WithoutLanguageHint_OmitsLanguageField()
    {
        const string fixture = """{"text":"x","language":"telugu","words":[]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture),
        });
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None);

        Assert.DoesNotContain("name=language", handler.LastRequestBody);
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
            provider.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio.mp3", null, CancellationToken.None));

        Assert.Contains("invalid file format", ex.Message);
    }
}
