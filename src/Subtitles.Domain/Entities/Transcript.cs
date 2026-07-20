namespace Subtitles.Domain.Entities;

/// <summary>One entry in a transcript's raw word-timestamp array (docs/Database.md §2.4).</summary>
public sealed record WordTimestamp(string Text, int StartMs, int EndMs);

/// <summary>
/// Raw speech-to-text output for a video — one row per video, replaced (not appended to)
/// on re-transcription. Never shown to creators directly; NativeCleanup turns this into
/// the Native subtitle track. See docs/Database.md §2.4.
/// </summary>
public class Transcript
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }
    public Video Video { get; set; } = null!;

    public string LanguageCode { get; set; } = null!;
    public string RawText { get; set; } = null!;
    public IReadOnlyList<WordTimestamp> WordTimestamps { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
