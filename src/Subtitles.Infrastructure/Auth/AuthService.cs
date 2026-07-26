using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    private const string UniqueViolationSqlState = "23505";

    public async Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);

        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
        {
            throw new EmailAlreadyRegisteredException(normalizedEmail);
        }

        var now = DateTimeOffset.UtcNow;
        var account = new Account { Id = Guid.NewGuid(), Name = displayName, CreatedAt = now };
        var user = new User
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Email = normalizedEmail,
            DisplayName = displayName,
            CreatedAt = now,
            PasswordHash = string.Empty,
        };
        user.PasswordHash = Hasher.HashPassword(user, password);

        db.Accounts.Add(account);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The AnyAsync check above is a look-then-act race: two concurrent registrations
            // for the same email can both pass it before either commits. The unique index is
            // the real guarantee; this turns its violation into the intended 409 response
            // instead of an unhandled 500.
            throw new EmailAlreadyRegisteredException(normalizedEmail);
        }

        return BuildResult(user);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct)
            ?? throw new InvalidCredentialsException();

        var verification = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hasher's parameters changed since this hash was created (e.g. a work-factor
            // upgrade) — persist a freshly-hashed value now so this migrates login by login
            // instead of never migrating at all.
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
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

    /// <summary>
    /// Lowercase + trim so "Creator@x.com" and "creator@x.com " are the same account — Postgres
    /// text comparison/uniqueness is case-sensitive by default, so without this, both the
    /// unique index and login-by-email would silently treat them as different users.
    /// </summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };

    private AuthResult BuildResult(User user) =>
        new(user.Id, user.AccountId, tokens.IssueAccessToken(user), tokens.IssueRefreshToken(user));
}
