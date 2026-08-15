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

    /// <summary>
    /// Optional creator-provided language hint (ISO-639-1, e.g. "te"), set at upload time.
    /// When present, Transcribe passes it straight to the Speech-to-Text Provider to skip
    /// auto-detection and bias the decoder toward that language's vocabulary — a real accuracy
    /// lever for lower-resource languages, not just a UI nicety. Null means auto-detect, the
    /// default per docs/ProductRequirements.md §6.2 ("no manual language selection required").
    /// </summary>
    public string? LanguageHint { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Transcript? Transcript { get; set; }
    public ICollection<SubtitleTrack> SubtitleTracks { get; set; } = new List<SubtitleTrack>();
    public ICollection<Export> Exports { get; set; } = new List<Export>();
    public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
    public ICollection<AiGeneration> AiGenerations { get; set; } = new List<AiGeneration>();
}
