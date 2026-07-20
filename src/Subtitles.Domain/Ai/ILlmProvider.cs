namespace Subtitles.Domain.Ai;

/// <summary>
/// Schema-first, provider-agnostic LLM access. Every pipeline stage wants a parsed,
/// validated object back, not free text to regex out — see docs/Architecture.md §3.2.
/// Each concrete provider enforces the schema its own way (OpenAI via
/// response_format=json_schema, a future Claude implementation via forced tool-calling,
/// a future Gemini implementation via its native responseSchema) without leaking that
/// choice into callers.
/// </summary>
public interface ILlmProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
        where TResult : class;
}

public sealed record LlmStructuredRequest(
    string SystemPrompt,
    string UserPrompt,
    JsonSchemaSpec ResponseSchema,
    IReadOnlyDictionary<string, object>? ModelParams = null);

/// <summary>A named JSON Schema a structured-output call's response must satisfy.</summary>
public sealed record JsonSchemaSpec(string Name, string SchemaJson, bool Strict = true);

/// <summary>
/// Thrown when a provider's response can't be parsed into the requested schema. Carries the
/// raw response so it's inspectable without a live debugger; the caller's existing
/// processing_jobs retry/backoff handles this like any other transient failure.
/// </summary>
public sealed class LlmStructuredOutputException(string message, string rawResponse, Exception? inner = null)
    : Exception(message, inner)
{
    public string RawResponse { get; } = rawResponse;
}
