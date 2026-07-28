namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §2 GET /videos list-item shape.</summary>
public sealed record VideoSummary(
    Guid VideoId,
    string OriginalFileName,
    string Status,
    int? DurationSeconds,
    string? DetectedLanguageCode,
    DateTimeOffset CreatedAt);

/// <summary>
/// Matches docs/API.md §2 GET /videos/{id} shape — the only status mechanism (no push
/// channel), so this is what the SPA polls. Always lists all three track types even before
/// their row exists, defaulting to "Pending" (see VideosController.GetById), since a client
/// needs to know a track is *going to exist* to render its (locked) tab before it's Ready.
/// </summary>
public sealed record VideoDetail(
    Guid VideoId,
    string OriginalFileName,
    string Status,
    int? DurationSeconds,
    string? DetectedLanguageCode,
    decimal? DetectedLanguageConfidence,
    IReadOnlyList<TrackStatusSummary> Tracks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TrackStatusSummary(string TrackType, string Status);
