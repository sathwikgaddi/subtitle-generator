using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Ai;

/// <summary>
/// Idempotent startup seeder for prompt_versions — see docs/Architecture.md §3.3. Only ever
/// inserts version 1 for a task that has no rows yet; publishing an improved prompt after that
/// is a deliberate new row + flipping is_active, done directly against the DB, not by editing
/// the embedded template and redeploying.
/// </summary>
public class PromptSeeder(SubtitlesDbContext db)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedTaskAsync(PromptTask.NativeCleanup, "NativeCleanup.v1.txt", ct);
        await SeedTaskAsync(PromptTask.TranslateToEnglish, "TranslateToEnglish.v1.txt", ct);
        await SeedTaskAsync(PromptTask.Romanize, "Romanize.v1.txt", ct);
        await SeedTaskAsync(PromptTask.GenerateHighlights, "GenerateHighlights.v1.txt", ct);
    }

    private async Task SeedTaskAsync(PromptTask task, string embeddedFileName, CancellationToken ct)
    {
        var alreadySeeded = await db.PromptVersions.AnyAsync(p => p.Task == task, ct);
        if (alreadySeeded)
        {
            return;
        }

        db.PromptVersions.Add(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = task,
            Version = 1,
            Template = ReadEmbeddedTemplate(embeddedFileName),
            IsActive = true,
            Notes = "Initial seeded version.",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    private static string ReadEmbeddedTemplate(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Subtitles.Infrastructure.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
