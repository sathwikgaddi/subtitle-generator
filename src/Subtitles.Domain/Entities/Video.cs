namespace Subtitles.Domain.Entities;

public class Video
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;
    public string BlobPath { get; set; } = null!;
    public string? AudioBlobPath { get; set; }
    public int? DurationSeconds { get; set; }

    public VideoStatus Status { get; set; } = VideoStatus.Uploaded;

    public string? DetectedLanguageCode { get; set; }
    public decimal? DetectedLanguageConfidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Transcript? Transcript { get; set; }
    public ICollection<SubtitleTrack> SubtitleTracks { get; set; } = new List<SubtitleTrack>();
    public ICollection<Export> Exports { get; set; } = new List<Export>();
    public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
    public ICollection<AiGeneration> AiGenerations { get; set; } = new List<AiGeneration>();
}
