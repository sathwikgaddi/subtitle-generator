using Microsoft.EntityFrameworkCore;
using Subtitles.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Subtitles.IntegrationTests.TestSupport;

/// <summary>
/// Spins up a real, throwaway PostgreSQL instance in Docker for the duration of a test
/// class — not the local dev database from docker-compose. Requires Docker running; this is
/// how docs/Architecture.md §9's "Testcontainers for a real Postgres" decision gets used.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("subtitles_test")
        .WithUsername("subtitles")
        .WithPassword("subtitles")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public SubtitlesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SubtitlesDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SubtitlesDbContext(options);
    }
}
