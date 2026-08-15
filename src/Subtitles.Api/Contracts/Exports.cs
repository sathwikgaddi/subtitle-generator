namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §4 POST /videos/{id}/export request.</summary>
public sealed record CreateExportRequest(string SubtitleTrackType, string Format);

/// <summary>Matches docs/API.md §4 POST /videos/{id}/export response — deliberately smaller
/// than ExportResponse, matching the documented contract exactly.</summary>
public sealed record CreateExportResponse(Guid ExportId, string Status);

/// <summary>
/// Matches docs/API.md §4 GET /exports/{id} — DownloadUrl points at our own
/// /api/v1/exports/{id}/download endpoint rather than a cloud SAS URL, since local-disk
/// storage has no such mechanism (same adaptation as P1.1's upload flow); null until Ready.
/// </summary>
public sealed record ExportResponse(
    Guid ExportId,
    Guid VideoId,
    string? SubtitleTrackType,
    string Format,
    string Status,
    string? DownloadUrl,
    DateTimeOffset CreatedAt);
