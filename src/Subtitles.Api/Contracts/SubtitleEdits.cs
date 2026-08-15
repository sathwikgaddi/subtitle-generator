namespace Subtitles.Api.Contracts;

/// <summary>Matches docs/API.md §3 PATCH .../cues/{cueId}.</summary>
public sealed record UpdateCueTextRequest(string Text);

/// <summary>Matches docs/API.md §3 PATCH .../words/{wordId}/highlight.</summary>
public sealed record UpdateWordHighlightRequest(bool Highlighted);

/// <summary>
/// Distinct from SubtitleWordResponse (the read-side shape) — this one reports whether the
/// highlight came from a manual override or the auto stage, per docs/API.md §3.
/// </summary>
public sealed record WordHighlightResponse(Guid WordId, string Text, bool IsHighlighted, string Source);
