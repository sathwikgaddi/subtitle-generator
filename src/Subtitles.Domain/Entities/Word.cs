using System.ComponentModel.DataAnnotations.Schema;

namespace Subtitles.Domain.Entities;

/// <summary>
/// Word-level breakdown of a cue, used for highlight rendering. Auto and manual highlight
/// state are separate columns so a manual override survives GenerateHighlights re-runs —
/// see docs/Database.md §2.7.
/// </summary>
public class Word
{
    public Guid Id { get; set; }

    public Guid CueId { get; set; }
    public SubtitleCue Cue { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public string Text { get; set; } = null!;

    public int? StartTimeMs { get; set; }
    public int? EndTimeMs { get; set; }

    /// <summary>Set by the GenerateHighlights pipeline stage — only that stage ever writes this.</summary>
    public bool IsHighlightedAuto { get; set; }

    /// <summary>
    /// true = creator forced highlight on, false = forced off, null = no manual override
    /// (defer to IsHighlightedAuto). Only manual edit endpoints ever write this.
    /// </summary>
    public bool? IsHighlightedManualOverride { get; set; }

    [NotMapped]
    public bool IsHighlighted => IsHighlightedManualOverride ?? IsHighlightedAuto;

    public void SetManualHighlight(bool? highlighted) => IsHighlightedManualOverride = highlighted;
}
