using System.ComponentModel.DataAnnotations;

namespace Subtitles.Infrastructure.Ai.Sarvam;

/// <summary>Same config-driven-model rule as the OpenAI options classes.</summary>
public class SarvamSttOptions
{
    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;
}
