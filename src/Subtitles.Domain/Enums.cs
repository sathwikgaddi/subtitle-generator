namespace Subtitles.Domain;

public enum VideoStatus
{
    Uploaded,
    Processing,
    Ready,
    Failed,
}

public enum SubtitleTrackType
{
    Native,
    English,
    Romanized,
}

public enum SubtitleTrackStatus
{
    Pending,
    Ready,
    Failed,
}

public enum ExportFormat
{
    SRT,
    VTT,
    BurnedInMp4,
}

public enum ExportStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>Matches processing_jobs.job_type exactly — see docs/Architecture.md §2.3.</summary>
public enum JobType
{
    ExtractAudio,
    Transcribe,
    NativeCleanup,
    TranslateToEnglish,
    Romanize,
    GenerateHighlights,
    Export,
}

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>Matches prompt_versions.task — the four LLM pipeline stages.</summary>
public enum PromptTask
{
    NativeCleanup,
    TranslateToEnglish,
    Romanize,
    GenerateHighlights,
}

/// <summary>Matches ai_generations.stage — every pipeline stage that produces AI output.</summary>
public enum GenerationStage
{
    Transcribe,
    NativeCleanup,
    TranslateToEnglish,
    Romanize,
    GenerateHighlights,
}

/// <summary>
/// Conventional values for ai_generations.reason (docs/Database.md §2.11). Not a DB enum —
/// stored as free text — but centralized here so callers don't scatter magic strings.
/// </summary>
public static class GenerationReasons
{
    public const string Initial = "initial";
    public const string ManualRegeneration = "manual_regeneration";
    public const string PromptUpgradeReprocess = "prompt_upgrade_reprocess";
}
