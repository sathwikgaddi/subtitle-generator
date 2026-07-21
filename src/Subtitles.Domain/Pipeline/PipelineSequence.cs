namespace Subtitles.Domain.Pipeline;

/// <summary>
/// Encodes the fixed, sequential pipeline order from docs/Architecture.md §2.3 — each stage
/// knows nothing about what comes next; the worker looks it up here after a stage succeeds.
/// Export is on-demand (creator-triggered), not chained automatically after GenerateHighlights.
/// </summary>
public static class PipelineSequence
{
    private static readonly IReadOnlyDictionary<JobType, JobType?> NextStage = new Dictionary<JobType, JobType?>
    {
        [JobType.ExtractAudio] = JobType.Transcribe,
        [JobType.Transcribe] = JobType.NativeCleanup,
        [JobType.NativeCleanup] = JobType.TranslateToEnglish,
        [JobType.TranslateToEnglish] = JobType.Romanize,
        [JobType.Romanize] = JobType.GenerateHighlights,
        [JobType.GenerateHighlights] = null,
        [JobType.Export] = null,
    };

    /// <summary>Null means this stage is the end of the automatic chain.</summary>
    public static JobType? GetNextStage(JobType current) => NextStage[current];
}
