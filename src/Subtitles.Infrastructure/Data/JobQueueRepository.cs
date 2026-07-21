using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data;

/// <summary>
/// This class *is* the job queue — see docs/Architecture.md §2.3 and §6.2. No message
/// broker: processing_jobs is polled directly with SELECT ... FOR UPDATE SKIP LOCKED, which
/// lets multiple worker instances poll the same table concurrently without ever grabbing the
/// same row (verified by JobQueueRepositoryConcurrencyTests).
/// </summary>
public class JobQueueRepository(SubtitlesDbContext db)
{
    private static readonly TimeSpan[] BackoffBySchedule =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    private const int MaxAttempts = 4;

    public async Task<ProcessingJob> EnqueueAsync(Guid videoId, JobType jobType, CancellationToken ct)
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            JobType = jobType,
            Status = JobStatus.Queued,
            AttemptCount = 0,
            AvailableAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    /// <summary>
    /// Locks and claims the oldest eligible job, or null if none is available. The lock is
    /// only held for the duration of this method's own transaction — by the time it returns,
    /// the row is already marked Running, so the lock itself doesn't need to be held any
    /// longer than that.
    /// </summary>
    public async Task<ProcessingJob?> TryDequeueAsync(string workerId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var queuedStatus = JobStatus.Queued.ToString();
        var job = await db.ProcessingJobs
            .FromSqlInterpolated($@"
                SELECT * FROM processing_jobs
                WHERE status = {queuedStatus} AND available_at <= now()
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED")
            .SingleOrDefaultAsync(ct);

        if (job is null)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        job.Status = JobStatus.Running;
        job.LockedBy = workerId;
        job.LockedAt = DateTimeOffset.UtcNow;
        job.StartedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return job;
    }

    /// <summary>
    /// Marks a job succeeded and enqueues the next stage in the same transaction — a worker
    /// crash between "mark done" and "enqueue next" must never happen as two separate,
    /// independently-failable writes, or a video can get stuck forever. See docs plan risk #9.
    /// </summary>
    public async Task CompleteAndEnqueueNextAsync(Guid jobId, JobType? nextJobType, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var job = await db.ProcessingJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found.");

        job.Status = JobStatus.Succeeded;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.LockedBy = null;
        job.LockedAt = null;

        if (nextJobType is not null)
        {
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Id = Guid.NewGuid(),
                VideoId = job.VideoId,
                JobType = nextJobType.Value,
                Status = JobStatus.Queued,
                AttemptCount = 0,
                AvailableAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Records a failure. Retries with backoff up to MaxAttempts, then leaves the job
    /// permanently Failed for the creator-facing retry UX (Roadmap.md Phase 1, UserFlows §9)
    /// to surface later.
    /// </summary>
    public async Task FailAsync(Guid jobId, string errorMessage, CancellationToken ct)
    {
        var job = await db.ProcessingJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found.");

        job.AttemptCount++;
        job.ErrorMessage = errorMessage;
        job.LockedBy = null;
        job.LockedAt = null;

        if (job.AttemptCount >= MaxAttempts)
        {
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            job.Status = JobStatus.Queued;
            job.AvailableAt = DateTimeOffset.UtcNow.Add(BackoffBySchedule[job.AttemptCount - 1]);
        }

        await db.SaveChangesAsync(ct);
    }
}
