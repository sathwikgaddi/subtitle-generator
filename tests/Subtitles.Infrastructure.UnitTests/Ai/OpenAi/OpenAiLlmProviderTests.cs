using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;
using Subtitles.Infrastructure.Ai.OpenAi;
using Subtitles.Infrastructure.UnitTests.TestSupport;
using Xunit;

namespace Subtitles.Infrastructure.UnitTests.Ai.OpenAi;

public class OpenAiLlmProviderTests
{
    private sealed record TestResult(string CleanedText, bool WasEdited);

    private static readonly JsonSchemaSpec TestSchema = new(
        "test_result",
        """
        {
          "type": "object",
          "properties": {
            "cleanedText": { "type": "string" },
            "wasEdited": { "type": "boolean" }
          },
          "required": ["cleanedText", "wasEdited"],
          "additionalProperties": false
        }
        """);

    private static OpenAiLlmProvider CreateProvider(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var options = Options.Create(new OpenAiLlmOptions { ApiKey = "test-key", Model = "test-model" });
        return new OpenAiLlmProvider(httpClient, options);
    }

    /// <summary>
    /// Builds a fixture matching OpenAI's Chat Completions envelope, where the structured
    /// payload is a JSON *string* nested inside choices[0].message.content — this
    /// double-encoding is real API behavior, not a test artifact.
    /// </summary>
    private static string BuildEnvelope(object innerContent)
    {
        var innerJson = JsonSerializer.Serialize(innerContent);
        return JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = innerJson } } },
        });
    }

    [Fact]
    public async Task CompleteStructuredAsync_WithValidResponse_ReturnsDeserializedResult()
    {
        var envelope = BuildEnvelope(new { cleanedText = "Hello there.", wasEdited = true });
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope),
        });
        var provider = CreateProvider(handler);

        var result = await provider.CompleteStructuredAsync<TestResult>(
            new LlmStructuredRequest("system prompt", "user prompt", TestSchema), CancellationToken.None);

        Assert.Equal("Hello there.", result.CleanedText);
        Assert.True(result.WasEdited);
    }

    [Fact]
    public async Task CompleteStructuredAsync_SendsBearerTokenAndModelName()
    {
        var envelope = BuildEnvelope(new { cleanedText = "x", wasEdited = false });
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope),
        });
        var provider = CreateProvider(handler);

        await provider.CompleteStructuredAsync<TestResult>(
            new LlmStructuredRequest("system", "user", TestSchema), CancellationToken.None);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.Contains("\"model\":\"test-model\"", handler.LastRequestBody);
        Assert.Contains("\"strict\":true", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteStructuredAsync_WithHttpErrorStatus_ThrowsWithRawResponseAttached()
    {
        const string errorBody = """{"error":{"message":"insufficient_quota"}}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(errorBody),
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<LlmStructuredOutputException>(() =>
            provider.CompleteStructuredAsync<TestResult>(
                new LlmStructuredRequest("system", "user", TestSchema), CancellationToken.None));

        Assert.Contains("insufficient_quota", ex.RawResponse);
    }

    [Fact]
    public async Task CompleteStructuredAsync_WithMalformedContent_ThrowsWithRawResponseAttached()
    {
        // content is not valid JSON at all — simulates the model breaking schema adherence.
        var envelope = """{"choices":[{"message":{"content":"not json"}}]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope),
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<LlmStructuredOutputException>(() =>
            provider.CompleteStructuredAsync<TestResult>(
                new LlmStructuredRequest("system", "user", TestSchema), CancellationToken.None));

        Assert.Contains("not json", ex.RawResponse);
    }
}
