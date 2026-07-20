using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Subtitles.Infrastructure.Data;

/// <summary>
/// Lets `dotnet ef migrations add` run against this project directly (`-p` and `-s` both
/// pointing at Subtitles.Infrastructure) without needing Api/Worker as a startup project.
/// Connection string comes from SUBTITLES_DB_CONNECTION_STRING, falling back to the local
/// docker-compose Postgres — see deploy/docker-compose.yml and deploy/.env.example.
/// </summary>
public class SubtitlesDbContextFactory : IDesignTimeDbContextFactory<SubtitlesDbContext>
{
    public SubtitlesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUBTITLES_DB_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=subtitles;Username=subtitles;Password=changeme-local-dev-only";

        var optionsBuilder = new DbContextOptionsBuilder<SubtitlesDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new SubtitlesDbContext(optionsBuilder.Options);
    }
}
