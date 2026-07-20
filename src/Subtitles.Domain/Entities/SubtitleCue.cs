namespace Subtitles.Domain.Entities;

/// <summary>
/// A line of a subtitle track. Timing is authoritative on the Native track (set during
/// NativeCleanup segmentation); English/Romanized cues share sequence_number/timing by
/// construction. See docs/Database.md §2.6.
/// </summary>
public class SubtitleCue
{
    public Guid Id { get; set; }

    public Guid SubtitleTrackId { get; set; }
    public SubtitleTrack SubtitleTrack { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public int StartTimeMs { get; set; }
    public int EndTimeMs { get; set; }

    /// <summary>Original machine-generated text — never overwritten, kept for diffing/reset.</summary>
    public string GeneratedText { get; set; } = null!;

    /// <summary>Set when a creator manually corrects the line; null means "use GeneratedText".</summary>
    public string? EditedText { get; set; }

    /// <summary>
    /// Real, independently-settable column (not derived from EditedText on read) so it stays
    /// cheaply filterable in SQL — see docs/Database.md §2.6. Kept in sync via ApplyManualEdit.
    /// </summary>
    public bool IsManuallyEdited { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Word> Words { get; set; } = new List<Word>();

    /// <summary>The text to render: the manual edit if present, otherwise the generated text.</summary>
    public string Text => EditedText ?? GeneratedText;

    public void ApplyManualEdit(string text, DateTimeOffset now)
    {
        EditedText = text;
        IsManuallyEdited = true;
        UpdatedAt = now;
    }
}
