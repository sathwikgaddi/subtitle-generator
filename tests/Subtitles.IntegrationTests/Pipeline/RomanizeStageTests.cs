using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Data;
using Subtitles.Infrastructure.Pipeline;
using Subtitles.IntegrationTests.TestSupport;
using Xunit;

namespace Subtitles.IntegrationTests.Pipeline;

/// <summary>
/// Uses a fake ILlmProvider — this test is about RomanizeStage's own DB orchestration
/// (inheriting timing from Native cues, language_code with the -Latn script hint, upsert-on-
/// retry), not about the LLM itself. The real OpenAI call is covered separately.
/// </summary>
public class RomanizeStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeLlmProvider(RomanizeResult result) : ILlmProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake-model";

        public Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
            where TResult : class
            => Task.FromResult((TResult)(object)result);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidNativeTrack_CreatesRomanizedTrackWithScriptHintLanguageCode()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        var promptVersionId = await SeedActivePromptVersionAsync(db);
        var (videoId, _) = await SeedVideoWithNativeCuesAsync(db, accountId, userId);

        var result = new RomanizeResult(["Namaskaram prapancham", "Meeru ela unnaru"]);
        var stage = new RomanizeStage(db, new FakeLlmProvider(result));
        await stage.ExecuteAsync(videoId, CancellationToken.None);

        var romanizedTrack = await db.SubtitleTracks
            .Include(t => t.Cues).ThenInclude(c => c.Words)
            .SingleAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Romanized);

        Assert.Equal("te-Latn", romanizedTrack.LanguageCode);
        Assert.Equal(SubtitleTrackStatus.Ready, romanizedTrack.Status);
        Assert.Equal(2, romanizedTrack.Cues.Count);

        var firstCue = romanizedTrack.Cues.Single(c => c.SequenceNumber == 1);
        Assert.Equal(0, firstCue.StartTimeMs);
        Assert.Equal(600, firstCue.EndTimeMs);
        Assert.Equal("Namaskaram prapancham", firstCue.GeneratedText);
        Assert.Equal(2, firstCue.Words.Count);
        Assert.All(firstCue.Words, w => Assert.Null(w.StartTimeMs));

        var generation = await db.AiGenerations
            .SingleAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.Romanize);
        Assert.Equal(romanizedTrack.Id, generation.SubtitleTrackId);
        Assert.Equal(promptVersionId, generation.PromptVersionId);
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedTransliterationCount_Throws()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        await SeedActivePromptVersionAsync(db);
        var (videoId, _) = await SeedVideoWithNativeCuesAsync(db, accountId, userId);

        var result = new RomanizeResult(["Just one."]);
        var stage = new RomanizeStage(db, new FakeLlmProvider(result));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => stage.ExecuteAsync(videoId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WhenRomanizedTrackAlreadyExists_ReplacesCuesRatherThanDuplicating()
    {
        await using var seedDb = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(seedDb);
        await SeedActivePromptVersionAsync(seedDb);
        var (videoId, _) = await SeedVideoWithNativeCuesAsync(seedDb, accountId, userId);

        await using (var db1 = fixture.CreateDbContext())
        {
            var firstResult = new RomanizeResult(["Namaskaram prapancham", "Meeru ela unnaru"]);
            var firstStage = new RomanizeStage(db1, new FakeLlmProvider(firstResult));
            await firstStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        await using (var db2 = fixture.CreateDbContext())
        {
            var secondResult = new RomanizeResult(["Namaste prapancham", "Meeru ela unnaru ra"]);
            var secondStage = new RomanizeStage(db2, new FakeLlmProvider(secondResult));
            await secondStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        await using var assertDb = fixture.CreateDbContext();
        var tracks = await assertDb.SubtitleTracks
            .Where(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Romanized)
            .ToListAsync();
        Assert.Single(tracks);

        var cues = await assertDb.SubtitleCues
            .Where(c => c.SubtitleTrackId == tracks[0].Id)
            .OrderBy(c => c.SequenceNumber)
            .ToListAsync();
        Assert.Equal(2, cues.Count);
        Assert.Equal("Namaste prapancham", cues[0].GeneratedText);
    }

    private static async Task<(Guid AccountId, Guid UserId)> SeedAccountAsync(SubtitlesDbContext db)
    {
        var account = new Account { Id = Guid.NewGuid(), Name = "Test Account", CreatedAt = DateTimeOffset.UtcNow };
        var user = new User
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Email = $"{Guid.NewGuid()}@test.local",
            PasswordHash = "not-a-real-hash",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (account.Id, user.Id);
    }

    /// <summary>Idempotent, like PromptSeeder — PostgresFixture's Postgres container (and the
    /// partial-unique-active-per-task index) is shared across every [Fact] in this class.</summary>
    private static async Task<Guid> SeedActivePromptVersionAsync(SubtitlesDbContext db)
    {
        var existing = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.Romanize && p.IsActive);
        if (existing is not null)
        {
            return existing.Id;
        }

        var promptVersion = new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = PromptTask.Romanize,
            Version = 1,
            Template = "test template",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PromptVersions.Add(promptVersion);
        await db.SaveChangesAsync();
        return promptVersion.Id;
    }

    private static async Task<(Guid VideoId, Guid NativeTrackId)> SeedVideoWithNativeCuesAsync(
        SubtitlesDbContext db, Guid accountId, Guid userId)
    {
        var videoId = Guid.NewGuid();

        db.Videos.Add(new Video
        {
            Id = videoId,
            AccountId = accountId,
            UploadedByUserId = userId,
            OriginalFileName = "input.mp4",
            BlobPath = "unused",
            AudioBlobPath = "unused",
            Status = VideoStatus.Processing,
            DetectedLanguageCode = "te",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var nativeTrack = new SubtitleTrack
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            TrackType = SubtitleTrackType.Native,
            LanguageCode = "te",
            Status = SubtitleTrackStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.SubtitleTracks.Add(nativeTrack);

        db.SubtitleCues.AddRange(
            new SubtitleCue
            {
                Id = Guid.NewGuid(),
                SubtitleTrackId = nativeTrack.Id,
                SequenceNumber = 1,
                StartTimeMs = 0,
                EndTimeMs = 600,
                GeneratedText = "నమస్కారం ప్రపంచం",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new SubtitleCue
            {
                Id = Guid.NewGuid(),
                SubtitleTrackId = nativeTrack.Id,
                SequenceNumber = 2,
                StartTimeMs = 600,
                EndTimeMs = 1200,
                GeneratedText = "మీరు ఎలా ఉన్నారు",
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        await db.SaveChangesAsync();
        return (videoId, nativeTrack.Id);
    }
}
