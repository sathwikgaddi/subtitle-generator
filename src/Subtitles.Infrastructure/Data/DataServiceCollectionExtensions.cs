using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Subtitles.Infrastructure.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddSubtitlesData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SubtitlesDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'SubtitlesDb' (ConnectionStrings:SubtitlesDb).");

        services.AddDbContext<SubtitlesDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        return services;
    }
}
