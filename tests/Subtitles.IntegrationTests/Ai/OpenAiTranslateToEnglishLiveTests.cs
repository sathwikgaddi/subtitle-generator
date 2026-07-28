using System.Reflection;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace Subtitles.IntegrationTests.Ai;

/// <summary>
/// Hits the real OpenAI API — costs a fraction of a cent per run. Excluded from normal
/// `dotnet test` / CI via the LiveOpenAI trait; run manually with:
/// dotnet test --filter "Category=LiveOpenAI"
///
/// Purpose: TranslateToEnglishStage's design bet is that the LLM reliably returns exactly one
/// translation per input cue, in order — that's what lets English cues inherit Native timing
/// without any re-derivation. This proves the actual prompt/schema hold that count exactly
/// against the real model, not a fake one.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiTranslateToEnglishLiveTests(ITestOutputHelper output)
{
    private static readonly string[] NativeCues =
    [
        "నమస్కారం, ఈ రోజు మనం కొత్త ఫీచర్ల గురించి మాట్లాడుకుందాం.",
        "ముందుగా, యాప్‌ను ఎలా ఇన్‌స్టాల్ చేయాలో చూద్దాం.",
        "ధన్యవాదాలు!",
    ];

    [Fact]
    public async Task CompleteStructuredAsync_AgainstRealOpenAi_ReturnsOneTranslationPerCue()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set in the environment. Export it before running LiveOpenAI tests.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_TEST_MODEL") ?? "gpt-5.6-terra";

        using var loggingHandler = new LoggingDelegatingHandler(output.WriteLine);
        using var httpClient = new HttpClient(loggingHandler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var options = Options.Create(new OpenAiLlmOptions { ApiKey = apiKey, Model = model });
        var provider = new OpenAiLlmProvider(httpClient, options);

        var systemPrompt = ReadEmbeddedTemplate("TranslateToEnglish.v1.txt");
        var userPrompt = string.Join('\n', NativeCues.Select((c, i) => $"{i + 1}: {c}"));

        var result = await provider.CompleteStructuredAsync<TranslateToEnglishResult>(
            new LlmStructuredRequest(systemPrompt, userPrompt, TranslateToEnglishResult.Schema), CancellationToken.None);

        output.WriteLine("--- PARSED RESULT ---");
        for (var i = 0; i < result.Translations.Count; i++)
        {
            output.WriteLine($"[{i + 1}] native: {NativeCues[i]}");
            output.WriteLine($"[{i + 1}] english: {result.Translations[i]}");
        }

        Assert.Equal(NativeCues.Length, result.Translations.Count);
        Assert.All(result.Translations, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    private static string ReadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(TranslateToEnglishStage).Assembly;
        var resourceName = $"Subtitles.Infrastructure.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
