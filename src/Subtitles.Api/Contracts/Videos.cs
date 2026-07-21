namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §2 GET /videos list-item shape.</summary>
public sealed record VideoSummary(
    Guid VideoId,
    string OriginalFileName,
    string Status,
    int? DurationSeconds,
    string? DetectedLanguageCode,
    DateTimeOffset CreatedAt);
