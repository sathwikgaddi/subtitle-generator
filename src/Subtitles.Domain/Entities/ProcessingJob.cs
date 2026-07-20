namespace Subtitles.Domain.Entities;

/// <summary>
/// This table IS the job queue, not just an audit log — the Worker polls it directly with
/// SELECT ... FOR UPDATE SKIP LOCKED. There is no message broker. See docs/Architecture.md
/// §2.3, §6.2 and docs/Database.md §2.9.
/// </summary>
public class ProcessingJob
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }
    public Video Video { get; set; } = null!;

    public JobType JobType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;

    public int AttemptCount { get; set; }

    /// <summary>Row is only eligible to be dequeued when AvailableAt &lt;= now(). Backoff pushes this forward.</summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>Worker instance id while Status == Running; cleared on completion/failure.</summary>
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
