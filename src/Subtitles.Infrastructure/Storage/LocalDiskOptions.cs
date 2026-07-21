using System.ComponentModel.DataAnnotations;

namespace Subtitles.Infrastructure.Storage;

public class LocalDiskOptions
{
    /// <summary>Root folder all video/audio files are stored under. Created if missing.</summary>
    [Required]
    public string RootPath { get; set; } = null!;
}
