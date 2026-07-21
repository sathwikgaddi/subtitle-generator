using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Auth;

public sealed record AuthResult(Guid UserId, Guid AccountId, string AccessToken, string RefreshToken);

/// <summary>
/// Register/login/refresh — see docs/API.md §1. Deliberately not full ASP.NET Core Identity
/// (UserManager/AspNetUsers): docs/Database.md §2.2's `users` table is a minimal, specific
/// shape, so this uses just PasswordHasher&lt;User&gt; (Identity's standalone hashing
/// primitive) directly against that table instead of Identity's full store/manager stack.
/// </summary>
public class AuthService(SubtitlesDbContext db, JwtTokenService tokens)
{
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new EmailAlreadyRegisteredException(email);
        }

        var now = DateTimeOffset.UtcNow;
        var account = new Account { Id = Guid.NewGuid(), Name = displayName, CreatedAt = now };
        var user = new User
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Email = email,
            DisplayName = displayName,
            CreatedAt = now,
            PasswordHash = string.Empty,
        };
        user.PasswordHash = Hasher.HashPassword(user, password);

        db.Accounts.Add(account);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return BuildResult(user);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new InvalidCredentialsException();

        var verification = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        return BuildResult(user);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var principal = tokens.ValidateRefreshToken(refreshToken)
            ?? throw new InvalidRefreshTokenException();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            throw new InvalidRefreshTokenException();
        }

        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidRefreshTokenException();

        return BuildResult(user);
    }

    private AuthResult BuildResult(User user) =>
        new(user.Id, user.AccountId, tokens.IssueAccessToken(user), tokens.IssueRefreshToken(user));
}
