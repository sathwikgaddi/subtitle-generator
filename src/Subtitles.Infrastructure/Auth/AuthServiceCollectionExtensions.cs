using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Subtitles.Infrastructure.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddSubtitlesAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton, not scoped: JwtBearerOptions (configured via IOptions<JwtBearerOptions>)
        // is resolved from the root provider by ASP.NET Core's authentication handler, which
        // cannot depend on a scoped service — see Subtitles.Api/Program.cs's JwtBearerOptions
        // configuration. JwtTokenService has no scoped dependencies of its own, so this is safe.
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<AuthService>();

        return services;
    }
}
