namespace Subtitles.Domain.Entities;

/// <summary>A generated, downloadable artifact for a video. See docs/Database.md §2.8.</summary>
public class Export
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }
    public Video Video { get; set; } = null!;

    /// <summary>Which output type this renders; null for a burned-in export (its own choice baked in).</summary>
    public SubtitleTrackType? SubtitleTrackType { get; set; }

    public ExportFormat Format { get; set; }
    public ExportStatus Status { get; set; } = ExportStatus.Pending;
    public string? BlobPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
