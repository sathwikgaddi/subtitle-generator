using System.ComponentModel.DataAnnotations;

namespace Subtitles.Infrastructure.Ai.OpenAi;

/// <summary>
/// Model is config-driven (Ai:Llm:OpenAi:Model in appsettings), never hard-coded in code —
/// see docs/Architecture.md §3.2. ApiKey is bound separately from the OPENAI_API_KEY
/// environment variable, never checked into appsettings.json.
/// </summary>
public class OpenAiLlmOptions
{
    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;
}
