using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Ai;
using Subtitles.Infrastructure.Data;
using Subtitles.IntegrationTests.TestSupport;
using Xunit;

namespace Subtitles.IntegrationTests.Ai;

/// <summary>
/// PostgresFixture's container (and prompt_versions' partial-unique-active-per-task index) is
/// shared across every [Fact] in this class, not reset between them — each test clears its own
/// task's rows first so results don't depend on execution order.
/// </summary>
public class PromptSeederTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static async Task ClearNativeCleanupPromptVersionsAsync(SubtitlesDbContext db)
    {
        await db.PromptVersions.Where(p => p.Task == PromptTask.NativeCleanup).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SeedAsync_WithNoExistingPromptVersions_InsertsActiveVersionOne()
    {
        await using var db = fixture.CreateDbContext();
        await ClearNativeCleanupPromptVersionsAsync(db);
        var seeder = new PromptSeeder(db);

        await seeder.SeedAsync(CancellationToken.None);

        var promptVersion = await db.PromptVersions.SingleAsync(p => p.Task == PromptTask.NativeCleanup);
        Assert.Equal(1, promptVersion.Version);
        Assert.True(promptVersion.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(promptVersion.Template));
    }

    [Fact]
    public async Task SeedAsync_WhenAPromptVersionAlreadyExists_DoesNotInsertAnother()
    {
        await using var db = fixture.CreateDbContext();
        await ClearNativeCleanupPromptVersionsAsync(db);
        db.PromptVersions.Add(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = PromptTask.NativeCleanup,
            Version = 2,
            Template = "a hand-published improved version",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var seeder = new PromptSeeder(db);
        await seeder.SeedAsync(CancellationToken.None);

        var versions = await db.PromptVersions.Where(p => p.Task == PromptTask.NativeCleanup).ToListAsync();
        Assert.Single(versions);
        Assert.Equal(2, versions[0].Version);
    }
}
