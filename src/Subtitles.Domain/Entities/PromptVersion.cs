namespace Subtitles.Domain.Entities;

/// <summary>
/// The prompt registry — prompt text lives here, not in application code, so publishing an
/// improved prompt is a new row + flipping IsActive, not a deployment. Exactly one active
/// row per Task (enforced by a partial unique index). See docs/Architecture.md §3.3 and
/// docs/Database.md §2.10.
/// </summary>
public class PromptVersion
{
    public Guid Id { get; set; }

    public PromptTask Task { get; set; }
    public int Version { get; set; }
    public string Template { get; set; } = null!;

    /// <summary>e.g. { "temperature": 0.2 } — passed alongside the prompt to the LLM Provider.</summary>
    public string? ModelParamsJson { get; set; }

    public bool IsActive { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
