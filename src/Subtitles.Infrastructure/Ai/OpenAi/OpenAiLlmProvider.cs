using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Subtitles.Domain.Ai;

namespace Subtitles.Infrastructure.Ai.OpenAi;

/// <summary>
/// OpenAI implementation of <see cref="ILlmProvider"/> — enforces the requested schema via
/// OpenAI's response_format=json_schema (strict mode). Nothing about this leaks upward:
/// callers only ever see <see cref="ILlmProvider"/>. See docs/Architecture.md §3.2.
/// </summary>
public class OpenAiLlmProvider(HttpClient httpClient, IOptions<OpenAiLlmOptions> options) : ILlmProvider
{
    private readonly OpenAiLlmOptions _options = options.Value;

    public string ProviderName => "openai";
    public string ModelName => _options.Model;

    public async Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
        where TResult : class
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            ["response_format"] = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.ResponseSchema.Name,
                    strict = request.ResponseSchema.Strict,
                    schema = JsonSerializer.Deserialize<JsonElement>(request.ResponseSchema.SchemaJson),
                },
            },
        };

        if (request.ModelParams is not null)
        {
            foreach (var (key, value) in request.ModelParams)
            {
                requestBody[key] = value;
            }
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(requestBody),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmStructuredOutputException(
                $"OpenAI chat completion request failed with HTTP {(int)response.StatusCode}.", responseBody);
        }

        string? content;
        try
        {
            using var envelope = JsonDocument.Parse(responseBody);
            content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmStructuredOutputException("Could not read the OpenAI response envelope.", responseBody, ex);
        }

        if (string.IsNullOrEmpty(content))
        {
            throw new LlmStructuredOutputException("OpenAI response had no message content.", responseBody);
        }

        try
        {
            return JsonSerializer.Deserialize<TResult>(content, JsonSerializerOptions.Web)
                ?? throw new LlmStructuredOutputException("Deserialized structured output was null.", responseBody);
        }
        catch (JsonException ex)
        {
            throw new LlmStructuredOutputException("Could not parse structured output as the requested schema.", responseBody, ex);
        }
    }
}
