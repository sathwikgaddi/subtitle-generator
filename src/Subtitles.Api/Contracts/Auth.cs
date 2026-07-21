using System.ComponentModel.DataAnnotations;

namespace Subtitles.Api.Contracts;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required] string DisplayName);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record RefreshRequest([Required] string RefreshToken);

public sealed record AuthResponse(Guid UserId, Guid AccountId, string AccessToken, string RefreshToken);
