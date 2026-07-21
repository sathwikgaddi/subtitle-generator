using System.ComponentModel.DataAnnotations;

namespace Subtitles.Infrastructure.Auth;

public class JwtOptions
{
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    public string Issuer { get; set; } = "subtitles-api";
    public string Audience { get; set; } = "subtitles-clients";

    public int AccessTokenLifetimeMinutes { get; set; } = 30;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
}
