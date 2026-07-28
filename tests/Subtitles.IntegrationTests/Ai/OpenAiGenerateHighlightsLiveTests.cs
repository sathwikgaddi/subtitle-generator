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
/// Purpose: GenerateHighlightsStage's design bet is that the model can look at three parallel
/// (Native/English/Romanized) renderings of the same cue and pick out the SAME underlying
/// concept in each, indexed into each rendering's own word list. This proves that actually
/// happens against the real model, not a fake one — specifically that whatever it highlights
/// in English roughly corresponds to what it highlights in Native/Romanized, not an unrelated
/// word chosen independently per rendering.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiGenerateHighlightsLiveTests(ITestOutputHelper output)
{
    // Cue 1: an application name ("SubtitleGen") is the one word worth highlighting, present
    // (in some form) in all three renderings.
    private static readonly string[] NativeWords = ["ఈ", "రోజు", "మనం", "SubtitleGen", "గురించి", "మాట్లాడుకుందాం."];
    private static readonly string[] EnglishWords = ["Today", "we'll", "talk", "about", "SubtitleGen."];
    private static readonly string[] RomanizedWords = ["Ee", "roju", "manam", "SubtitleGen", "gurinchi", "maatladukundam."];

    [Fact]
    public async Task CompleteStructuredAsync_AgainstRealOpenAi_HighlightsTheSameConceptAcrossAllThreeRenderings()
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

        var systemPrompt = ReadEmbeddedTemplate("GenerateHighlights.v1.txt");
        var userPrompt =
            "Cue 1:\n" +
            $"Native: {string.Join(' ', NativeWords.Select((w, i) => $"{i}:{w}"))}\n" +
            $"English: {string.Join(' ', EnglishWords.Select((w, i) => $"{i}:{w}"))}\n" +
            $"Romanized: {string.Join(' ', RomanizedWords.Select((w, i) => $"{i}:{w}"))}\n";

        var result = await provider.CompleteStructuredAsync<GenerateHighlightsResult>(
            new LlmStructuredRequest(systemPrompt, userPrompt, GenerateHighlightsResult.Schema), CancellationToken.None);

        var cue = Assert.Single(result.Cues);
        output.WriteLine("--- PARSED RESULT ---");
        output.WriteLine($"Native highlighted: {string.Join(", ", cue.HighlightedNativeWordIndices.Select(i => NativeWords[i]))}");
        output.WriteLine($"English highlighted: {string.Join(", ", cue.HighlightedEnglishWordIndices.Select(i => EnglishWords[i]))}");
        output.WriteLine($"Romanized highlighted: {string.Join(", ", cue.HighlightedRomanizedWordIndices.Select(i => RomanizedWords[i]))}");

        Assert.NotEmpty(cue.HighlightedNativeWordIndices);
        Assert.NotEmpty(cue.HighlightedEnglishWordIndices);
        Assert.NotEmpty(cue.HighlightedRomanizedWordIndices);

        // The one word that's spelled identically across all three renderings is the app
        // name — a real signal that it found the same concept in each, not three independent
        // (and possibly unrelated) picks.
        Assert.Contains(3, cue.HighlightedNativeWordIndices);
        Assert.Contains(3, cue.HighlightedRomanizedWordIndices);
        Assert.Contains(cue.HighlightedEnglishWordIndices, i => EnglishWords[i].Contains("SubtitleGen"));
    }

    private static string ReadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(GenerateHighlightsStage).Assembly;
        var resourceName = $"Subtitles.Infrastructure.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
