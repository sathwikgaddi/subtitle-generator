namespace Subtitles.Domain.Pipeline;

/// <summary>
/// One stage of the sequential AI pipeline (docs/Architecture.md §2.3). Implementations
/// live in Subtitles.Infrastructure; the Worker resolves one per JobType and must be
/// idempotent — see docs/Architecture.md §2.4.
/// </summary>
public interface IPipelineStage
{
    /// <summary>Matches ProcessingJob.JobType exactly.</summary>
    JobType JobType { get; }

    Task ExecuteAsync(Guid videoId, CancellationToken ct);
}
