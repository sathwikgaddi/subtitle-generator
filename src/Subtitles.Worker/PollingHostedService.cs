using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Worker;

/// <summary>
/// The Worker's main loop — polls processing_jobs on an interval, runs whichever
/// IPipelineStage matches the dequeued job's JobType, and completes/fails it. See
/// docs/Architecture.md §2.3.
/// </summary>
public class PollingHostedService(IServiceScopeFactory scopeFactory, ILogger<PollingHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    // Identifies this worker instance for processing_jobs.locked_by — useful for spotting a
    // crashed worker's stale lock later (Database.md §2.9), not load-bearing for correctness
    // (the FOR UPDATE SKIP LOCKED transaction is what actually prevents double-processing).
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} starting, polling every {Interval}", _workerId, PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processedSomething;
            try
            {
                processedSomething = await TryProcessOneJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!processedSomething)
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> TryProcessOneJobAsync(CancellationToken ct)
    {
        // A new DI scope per job: SubtitlesDbContext and any stage's own dependencies are
        // scoped, so each job gets a clean, independent set of them.
        using var scope = scopeFactory.CreateScope();
        var jobQueue = scope.ServiceProvider.GetRequiredService<JobQueueRepository>();

        var job = await jobQueue.TryDequeueAsync(_workerId, ct);
        if (job is null)
        {
            return false;
        }

        var stage = scope.ServiceProvider.GetServices<IPipelineStage>()
            .FirstOrDefault(s => s.JobType == job.JobType);

        if (stage is null)
        {
            logger.LogError("No IPipelineStage registered for job type {JobType}", job.JobType);
            await jobQueue.FailAsync(job.Id, $"No stage registered for job type '{job.JobType}'.", ct);
            return true;
        }

        try
        {
            await stage.ExecuteAsync(job.VideoId, ct);
            var nextStage = PipelineSequence.GetNextStage(job.JobType);
            await jobQueue.CompleteAndEnqueueNextAsync(job.Id, nextStage, ct);
            logger.LogInformation(
                "Completed {JobType} for video {VideoId}; next stage: {NextStage}",
                job.JobType, job.VideoId, nextStage?.ToString() ?? "(none)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed {JobType} for video {VideoId}", job.JobType, job.VideoId);
            await jobQueue.FailAsync(job.Id, ex.Message, ct);
        }

        return true;
    }
}
