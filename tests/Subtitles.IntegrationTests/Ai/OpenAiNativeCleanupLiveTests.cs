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
/// Purpose: NativeCleanupStage's design bet is that the LLM can be asked for cue boundaries as
/// word-INDEX ranges (not timestamps) and reliably tile the whole transcript with no gaps —
/// that's what makes deriving exact cue timing from real ASR data possible. This test proves
/// the actual prompt template + schema achieve that against the real model, not a fake one.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiNativeCleanupLiveTests(ITestOutputHelper output)
{
    // A disfluency-laden raw transcript, the kind of thing NativeCleanup exists to clean up.
    private static readonly string[] RawWords =
    [
        "um", "so", "like", "today", "we're", "gonna", "talk", "about", "the",
        "new", "uh", "features", "and", "how", "they", "you", "know", "actually", "work",
    ];

    [Fact]
    public async Task CompleteStructuredAsync_AgainstRealOpenAi_ProducesFullyTilingCues()
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

        var systemPrompt = ReadEmbeddedNativeCleanupTemplate();
        var userPrompt = string.Join('\n', RawWords.Select((w, i) => $"{i}: {w}"));

        var result = await provider.CompleteStructuredAsync<NativeCleanupResult>(
            new LlmStructuredRequest(systemPrompt, userPrompt, NativeCleanupResult.Schema), CancellationToken.None);

        output.WriteLine("--- PARSED RESULT ---");
        foreach (var cue in result.Cues)
        {
            output.WriteLine($"[{cue.StartWordIndex}-{cue.EndWordIndex}] {cue.Text}");
        }

        Assert.NotEmpty(result.Cues);

        // Same coverage rule NativeCleanupStage itself enforces at runtime — this is the
        // actual thing worth verifying against a real model, not just that JSON came back.
        var expectedNext = 0;
        foreach (var cue in result.Cues)
        {
            Assert.Equal(expectedNext, cue.StartWordIndex);
            Assert.True(cue.EndWordIndex >= cue.StartWordIndex && cue.EndWordIndex < RawWords.Length);
            expectedNext = cue.EndWordIndex + 1;
        }
        Assert.Equal(RawWords.Length, expectedNext);
    }

    private static string ReadEmbeddedNativeCleanupTemplate()
    {
        var assembly = typeof(NativeCleanupStage).Assembly;
        const string resourceName = "Subtitles.Infrastructure.Prompts.NativeCleanup.v1.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
