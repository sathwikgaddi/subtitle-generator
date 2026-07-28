namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §3 GET /videos/{id}/subtitles shape.</summary>
public sealed record SubtitleTrackResponse(
    string TrackType,
    string LanguageCode,
    string Status,
    GeneratedByInfo? GeneratedBy,
    IReadOnlyList<SubtitleCueResponse> Cues);

public sealed record GeneratedByInfo(
    string? LlmProvider,
    string? LlmModel,
    int? PromptVersion,
    DateTimeOffset GeneratedAt,
    string Reason);

public sealed record SubtitleCueResponse(
    Guid CueId,
    int SequenceNumber,
    int StartTimeMs,
    int EndTimeMs,
    string Text,
    bool IsManuallyEdited,
    IReadOnlyList<SubtitleWordResponse> Words);

public sealed record SubtitleWordResponse(Guid WordId, string Text, bool IsHighlighted);
