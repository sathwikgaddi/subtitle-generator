using System.ComponentModel.DataAnnotations;

namespace Subtitles.Infrastructure.Ai.OpenAi;

/// <summary>Same config-driven-model rule as <see cref="OpenAiLlmOptions"/>, for the Whisper API.</summary>
public class OpenAiSttOptions
{
    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;
}
