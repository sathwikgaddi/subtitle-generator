using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Subtitles.Infrastructure.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.Ai.Sarvam;
using Subtitles.Infrastructure.UnitTests.TestSupport;
using Xunit;

namespace Subtitles.Infrastructure.UnitTests.Ai;

public class RoutedSpeechToTextProviderTests
{
    private static (OpenAiSpeechToTextProvider Provider, FakeHttpMessageHandler Handler) CreateOpenAi()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"text":"from openai","language":"english","words":[]}"""),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var options = Options.Create(new OpenAiSttOptions { ApiKey = "k", Model = "whisper-1" });
        return (new OpenAiSpeechToTextProvider(httpClient, options, NullLogger<OpenAiSpeechToTextProvider>.Instance), handler);
    }

    private static (SarvamSpeechToTextProvider Provider, FakeHttpMessageHandler Handler) CreateSarvam()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"transcript":"from sarvam","language_code":"te-IN","timestamps":{"words":[],"start_time_seconds":[],"end_time_seconds":[]}}"""),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.sarvam.ai/") };
        var options = Options.Create(new SarvamSttOptions { ApiKey = "k", Model = "saaras:v4" });
        return (new SarvamSpeechToTextProvider(httpClient, options), handler);
    }

    private static RoutedSpeechToTextProvider CreateRouter(
        OpenAiSpeechToTextProvider openAi, SarvamSpeechToTextProvider sarvam, string defaultProvider, Dictionary<string, string>? byHint = null)
    {
        var options = Options.Create(new SpeechToTextRoutingOptions
        {
            DefaultProvider = defaultProvider,
            ProviderByLanguageHint = byHint ?? new Dictionary<string, string>(),
        });
        return new RoutedSpeechToTextProvider(openAi, sarvam, options);
    }

    [Fact]
    public async Task TranscribeAsync_WithHintMappedToSarvam_DelegatesToSarvam()
    {
        var (openAi, _) = CreateOpenAi();
        var (sarvam, _) = CreateSarvam();
        var router = CreateRouter(openAi, sarvam, defaultProvider: "OpenAi", byHint: new() { ["te"] = "Sarvam" });

        var result = await router.TranscribeAsync(new MemoryStream([1]), "audio.mp3", "te", CancellationToken.None);

        Assert.Equal("from sarvam", result.Text);
        Assert.Equal("sarvam", result.ProviderName);
    }

    [Fact]
    public async Task TranscribeAsync_WithHintMappedToOpenAi_DelegatesToOpenAi()
    {
        var (openAi, _) = CreateOpenAi();
        var (sarvam, _) = CreateSarvam();
        var router = CreateRouter(openAi, sarvam, defaultProvider: "Sarvam", byHint: new() { ["en"] = "OpenAi" });

        var result = await router.TranscribeAsync(new MemoryStream([1]), "audio.mp3", "en", CancellationToken.None);

        Assert.Equal("from openai", result.Text);
        Assert.Equal("openai", result.ProviderName);
    }

    [Fact]
    public async Task TranscribeAsync_WithNoHint_UsesDefaultProvider()
    {
        var (openAi, _) = CreateOpenAi();
        var (sarvam, _) = CreateSarvam();
        var router = CreateRouter(openAi, sarvam, defaultProvider: "Sarvam");

        var result = await router.TranscribeAsync(new MemoryStream([1]), "audio.mp3", null, CancellationToken.None);

        Assert.Equal("from sarvam", result.Text);
    }

    [Fact]
    public async Task TranscribeAsync_WithHintNotInMap_FallsBackToDefaultProvider()
    {
        var (openAi, _) = CreateOpenAi();
        var (sarvam, _) = CreateSarvam();
        var router = CreateRouter(openAi, sarvam, defaultProvider: "OpenAi", byHint: new() { ["en"] = "OpenAi" });

        // "hi" isn't in the map — should fall back to DefaultProvider, not throw.
        var result = await router.TranscribeAsync(new MemoryStream([1]), "audio.mp3", "hi", CancellationToken.None);

        Assert.Equal("from openai", result.Text);
    }
}
