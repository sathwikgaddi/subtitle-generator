namespace Subtitles.Domain.Entities;

/// <summary>
/// One row per (video, output type) — exactly three per fully-processed video:
/// Native, English, Romanized. See docs/Database.md §2.5.
/// </summary>
public class SubtitleTrack
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }
    public Video Video { get; set; } = null!;

    public SubtitleTrackType TrackType { get; set; }
    public string LanguageCode { get; set; } = null!;
    public SubtitleTrackStatus Status { get; set; } = SubtitleTrackStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SubtitleCue> Cues { get; set; } = new List<SubtitleCue>();
}
