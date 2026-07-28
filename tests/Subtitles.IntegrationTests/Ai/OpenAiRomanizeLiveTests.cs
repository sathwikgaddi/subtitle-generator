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
/// Purpose: proves the actual Romanize prompt/schema hold the cue count exactly against the
/// real model, and — the thing worth actually eyeballing — that it transliterates
/// (pronunciation) rather than translates (meaning), which the prompt explicitly demands but
/// only a real model call can confirm it actually follows.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiRomanizeLiveTests(ITestOutputHelper output)
{
    private static readonly string[] NativeCues =
    [
        "నమస్కారం, ఈ రోజు మనం కొత్త ఫీచర్ల గురించి మాట్లాడుకుందాం.",
        "ధన్యవాదాలు!",
    ];

    [Fact]
    public async Task CompleteStructuredAsync_AgainstRealOpenAi_ReturnsOneTransliterationPerCue()
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

        var systemPrompt = ReadEmbeddedTemplate("Romanize.v1.txt");
        var userPrompt = string.Join('\n', NativeCues.Select((c, i) => $"{i + 1}: {c}"));

        var result = await provider.CompleteStructuredAsync<RomanizeResult>(
            new LlmStructuredRequest(systemPrompt, userPrompt, RomanizeResult.Schema), CancellationToken.None);

        output.WriteLine("--- PARSED RESULT ---");
        for (var i = 0; i < result.Transliterations.Count; i++)
        {
            output.WriteLine($"[{i + 1}] native: {NativeCues[i]}");
            output.WriteLine($"[{i + 1}] romanized: {result.Transliterations[i]}");
        }

        Assert.Equal(NativeCues.Length, result.Transliterations.Count);
        Assert.All(result.Transliterations, t => Assert.False(string.IsNullOrWhiteSpace(t)));

        // Loose but real signal that this transliterated rather than translated: "namaskaram"
        // (or a close romanization of it) should survive into the output, but the English
        // word "hello" should not have replaced it with a translation instead.
        Assert.Contains("namaskar", result.Transliterations[0], StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(RomanizeStage).Assembly;
        var resourceName = $"Subtitles.Infrastructure.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
