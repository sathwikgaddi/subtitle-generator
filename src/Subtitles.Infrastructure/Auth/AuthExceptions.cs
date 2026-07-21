namespace Subtitles.Infrastructure.Auth;

public sealed class EmailAlreadyRegisteredException(string email)
    : Exception($"Email '{email}' is already registered.");

public sealed class InvalidCredentialsException() : Exception("Invalid email or password.");

public sealed class InvalidRefreshTokenException() : Exception("Refresh token is invalid or expired.");
