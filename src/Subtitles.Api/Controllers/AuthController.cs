using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Subtitles.Api.Contracts;
using Subtitles.Infrastructure.Auth;

namespace Subtitles.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await authService.RegisterAsync(request.Email, request.Password, request.DisplayName, ct);
            return StatusCode(StatusCodes.Status201Created, ToResponse(result));
        }
        catch (EmailAlreadyRegisteredException)
        {
            return Conflict(ApiError.Of("email_already_registered", "An account with this email already exists."));
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await authService.LoginAsync(request.Email, request.Password, ct);
            return Ok(ToResponse(result));
        }
        catch (InvalidCredentialsException)
        {
            return Unauthorized(ApiError.Of("invalid_credentials", "Invalid email or password."));
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        try
        {
            var result = await authService.RefreshAsync(request.RefreshToken, ct);
            return Ok(ToResponse(result));
        }
        catch (InvalidRefreshTokenException)
        {
            return Unauthorized(ApiError.Of("invalid_credentials", "Refresh token is invalid or expired."));
        }
    }

    private static AuthResponse ToResponse(AuthResult result) =>
        new(result.UserId, result.AccountId, result.AccessToken, result.RefreshToken);
}
