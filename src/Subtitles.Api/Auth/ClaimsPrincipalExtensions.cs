using System.Security.Claims;

namespace Subtitles.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetAccountId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("account_id")
            ?? throw new InvalidOperationException("Authenticated principal is missing the account_id claim.");
        return Guid.Parse(value);
    }
}
