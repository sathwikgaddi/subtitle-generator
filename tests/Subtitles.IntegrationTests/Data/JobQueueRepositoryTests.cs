using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Data;
using Subtitles.IntegrationTests.TestSupport;
using Xunit;

namespace Subtitles.IntegrationTests.Data;

/// <summary>
/// Against a real Postgres (Testcontainers) — the FOR UPDATE SKIP LOCKED behavior in
/// JobQueueRepository can't be meaningfully verified against a fake/in-memory database, since
/// it depends on real row-locking semantics. All tests share one PostgresFixture instance and
/// run sequentially (xUnit's default within a single class) so they don't interfere with each
/// other's rows.
/// </summary>
public class JobQueueRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> SeedVideoAsync()
    {
        await using var db = fixture.CreateDbContext();

        var account = new Account { Id = Guid.NewGuid(), Name = "Test Account", CreatedAt = DateTimeOffset.UtcNow };
        var user = new User
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Email = $"{Guid.NewGuid()}@test.local",
            PasswordHash = "not-a-real-hash",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var video = new Video
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            UploadedByUserId = user.Id,
            OriginalFileName = "test.mp4",
            BlobPath = "videos/test.mp4",
            Status = VideoStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        db.Users.Add(user);
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        return video.Id;
    }

    [Fact]
    public async Task TryDequeueAsync_WithNoQueuedJobs_ReturnsNull()
    {
        await using var db = fixture.CreateDbContext();
        var repo = new JobQueueRepository(db);

        var job = await repo.TryDequeueAsync("worker-1", CancellationToken.None);

        Assert.Null(job);
    }

    [Fact]
    public async Task EnqueueThenTryDequeue_MarksJobRunningAndLocked()
    {
        var videoId = await SeedVideoAsync();
        await using var db = fixture.CreateDbContext();
        var repo = new JobQueueRepository(db);

        var enqueued = await repo.EnqueueAsync(videoId, JobType.ExtractAudio, CancellationToken.None);
        var dequeued = await repo.TryDequeueAsync("worker-1", CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.Equal(enqueued.Id, dequeued.Id);
        Assert.Equal(JobStatus.Running, dequeued.Status);
        Assert.Equal("worker-1", dequeued.LockedBy);
        Assert.NotNull(dequeued.StartedAt);
    }

    [Fact]
    public async Task CompleteAndEnqueueNextAsync_MarksSucceededAndCreatesNextJob()
    {
        var videoId = await SeedVideoAsync();
        await using var db = fixture.CreateDbContext();
        var repo = new JobQueueRepository(db);

        var job = await repo.EnqueueAsync(videoId, JobType.ExtractAudio, CancellationToken.None);
        await repo.TryDequeueAsync("worker-1", CancellationToken.None);

        await repo.CompleteAndEnqueueNextAsync(job.Id, JobType.Transcribe, CancellationToken.None);

        await using var verifyDb = fixture.CreateDbContext();
        var completedJob = await verifyDb.ProcessingJobs.FindAsync(job.Id);
        var nextJob = await verifyDb.ProcessingJobs
            .Where(j => j.VideoId == videoId && j.JobType == JobType.Transcribe)
            .FirstOrDefaultAsync();

        Assert.Equal(JobStatus.Succeeded, completedJob!.Status);
        Assert.NotNull(completedJob.CompletedAt);
        Assert.NotNull(nextJob);
        Assert.Equal(JobStatus.Queued, nextJob!.Status);
    }

    [Fact]
    public async Task CompleteAndEnqueueNextAsync_WithNoNextStage_DoesNotCreateAnotherJob()
    {
        var videoId = await SeedVideoAsync();
        await using var db = fixture.CreateDbContext();
        var repo = new JobQueueRepository(db);

        var job = await repo.EnqueueAsync(videoId, JobType.GenerateHighlights, CancellationToken.None);
        await repo.TryDequeueAsync("worker-1", CancellationToken.None);

        await repo.CompleteAndEnqueueNextAsync(job.Id, nextJobType: null, CancellationToken.None);

        await using var verifyDb = fixture.CreateDbContext();
        var jobsForVideo = await verifyDb.ProcessingJobs.Where(j => j.VideoId == videoId).ToListAsync();

        Assert.Single(jobsForVideo);
        Assert.Equal(JobStatus.Succeeded, jobsForVideo[0].Status);
    }

    [Fact]
    public async Task FailAsync_BelowMaxAttempts_RequeuesWithBackoff()
    {
        var videoId = await SeedVideoAsync();
        await using var db = fixture.CreateDbContext();
        var repo = new JobQueueRepository(db);

        var job = await repo.EnqueueAsync(videoId, JobType.ExtractAudio, CancellationToken.None);
        await repo.TryDequeueAsync("worker-1", CancellationToken.None);

        await repo.FailAsync(job.Id, "transient error", CancellationToken.None);

        await using var verifyDb = fixture.CreateDbContext();
        var failed = await verifyDb.ProcessingJobs.FindAsync(job.Id);

        Assert.Equal(JobStatus.Queued, failed!.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.True(failed.AvailableAt > DateTimeOffset.UtcNow);
        Assert.Null(failed.LockedBy);
    }

    [Fact]
    public async Task TryDequeueAsync_WithManyConcurrentWorkers_NeverProcessesTheSameJobTwice()
    {
        var videoId = await SeedVideoAsync();

        const int jobCount = 20;
        await using (var seedDb = fixture.CreateDbContext())
        {
            var seedRepo = new JobQueueRepository(seedDb);
            for (var i = 0; i < jobCount; i++)
            {
                await seedRepo.EnqueueAsync(videoId, JobType.ExtractAudio, CancellationToken.None);
            }
        }

        // This is the scenario FOR UPDATE SKIP LOCKED exists to handle: many callers racing
        // against the same rows at the same instant, each looping until the queue is empty.
        var dequeuedJobIds = new ConcurrentBag<Guid>();

        async Task RunWorkerAsync(string workerId)
        {
            await using var db = fixture.CreateDbContext();
            var repo = new JobQueueRepository(db);
            while (true)
            {
                var job = await repo.TryDequeueAsync(workerId, CancellationToken.None);
                if (job is null)
                {
                    break;
                }

                dequeuedJobIds.Add(job.Id);
            }
        }

        var workers = Enumerable.Range(0, 8).Select(i => RunWorkerAsync($"worker-{i}"));
        await Task.WhenAll(workers);

        Assert.Equal(jobCount, dequeuedJobIds.Count);
        Assert.Equal(jobCount, dequeuedJobIds.Distinct().Count());
    }
}
