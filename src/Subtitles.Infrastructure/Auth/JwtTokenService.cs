using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Auth;

/// <summary>
/// Issues and validates access/refresh JWTs. Refresh tokens are stateless (a longer-lived
/// signed JWT distinguished by a "token_use" claim), not stored server-side — there is no
/// refresh_tokens table. This trades away pre-expiry revocation for avoiding an extra table
/// and matches this project's low-infrastructure MVP posture; revisit if that trade stops
/// being acceptable.
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public string IssueAccessToken(User user) =>
        IssueToken(user, TokenUse.Access, TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes));

    public string IssueRefreshToken(User user) =>
        IssueToken(user, TokenUse.Refresh, TimeSpan.FromDays(_options.RefreshTokenLifetimeDays));

    /// <summary>Validates a refresh token's signature, expiry, and that it's actually a refresh token.</summary>
    public ClaimsPrincipal? ValidateRefreshToken(string token) => Validate(token, TokenUse.Refresh);

    /// <summary>Validates an access token — used by JwtBearer configuration in Subtitles.Api.</summary>
    public TokenValidationParameters AccessTokenValidationParameters => BuildValidationParameters();

    private ClaimsPrincipal? Validate(string token, string expectedUse)
    {
        // Without this, JwtSecurityTokenHandler silently remaps short claim names ("sub" etc.)
        // to long legacy XML-schema URIs, so FindFirstValue(JwtRegisteredClaimNames.Sub) would
        // return null even for a perfectly valid token — keep claims as issued.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, BuildValidationParameters(), out _);
            var tokenUse = principal.FindFirstValue("token_use");
            return tokenUse == expectedUse ? principal : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private TokenValidationParameters BuildValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    private string IssueToken(User user, string tokenUse, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("account_id", user.AccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("token_use", tokenUse),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class TokenUse
{
    public const string Access = "access";
    public const string Refresh = "refresh";
}
