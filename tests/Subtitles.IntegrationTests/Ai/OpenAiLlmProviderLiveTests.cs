using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Xunit;
using Xunit.Abstractions;

namespace Subtitles.IntegrationTests.Ai;

/// <summary>
/// Logs the raw outgoing request body and raw incoming response body through the given
/// sink — wraps the real network handler so nothing about the actual HTTP traffic changes,
/// it's purely observational. Useful for LiveOpenAI tests when you want to see exactly what
/// went over the wire, not just the parsed C# result.
/// </summary>
public sealed class LoggingDelegatingHandler(Action<string> log) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            log($"--- REQUEST {request.Method} {request.RequestUri} ---\n{requestBody}");
        }

        var response = await base.SendAsync(request, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        log($"--- RESPONSE {(int)response.StatusCode} {response.StatusCode} ---\n{responseBody}");

        // The response content stream was already consumed above to log it — hand back a
        // fresh copy so the caller (OpenAiLlmProvider) can still read it normally.
        response.Content = new StringContent(responseBody);
        return response;
    }
}

/// <summary>
/// Hits the real OpenAI API — costs a fraction of a cent per run. Excluded from normal
/// `dotnet test` / CI via the LiveOpenAI trait (see .github/workflows/ci.yml); run manually
/// with: dotnet test --filter "Category=LiveOpenAI"
/// </summary>
[Trait("Category", "LiveOpenAI")]
public class OpenAiLlmProviderLiveTests(ITestOutputHelper output)
{
    private sealed record CleanupResult(string CleanedText);

    private static readonly JsonSchemaSpec CleanupSchema = new(
        "cleanup_result",
        """
        {
          "type": "object",
          "properties": {
            "cleanedText": { "type": "string" }
          },
          "required": ["cleanedText"],
          "additionalProperties": false
        }
        """);

    [Fact]
    public async Task CompleteStructuredAsync_AgainstRealOpenAi_ReturnsCleanedTranscript()
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

        const string systemPrompt =
            "You clean up raw speech-to-text transcripts: fix punctuation and casing, " +
            "remove filler disfluencies, without changing the meaning or adding content.";
        const string rawTranscript = "um so like today we're gonna talk about the new features okay";

        var result = await provider.CompleteStructuredAsync<CleanupResult>(
            new LlmStructuredRequest(systemPrompt, rawTranscript, CleanupSchema), CancellationToken.None);

        output.WriteLine($"--- PARSED RESULT ---");
        output.WriteLine($"Model:   {provider.ModelName}");
        output.WriteLine($"Raw:     {rawTranscript}");
        output.WriteLine($"Cleaned: {result.CleanedText}");

        Assert.False(string.IsNullOrWhiteSpace(result.CleanedText));
        Assert.NotEqual(rawTranscript, result.CleanedText);
    }
}
