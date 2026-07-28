namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §3 GET /videos/{id}/generations shape.</summary>
public sealed record VideoGenerationsResponse(Guid VideoId, IReadOnlyList<GenerationEntry> Generations);

public sealed record GenerationEntry(
    string Stage,
    string? SpeechProvider,
    string? SpeechModel,
    string? LlmProvider,
    string? LlmModel,
    int? PromptVersion,
    DateTimeOffset GeneratedAt,
    string Reason);
