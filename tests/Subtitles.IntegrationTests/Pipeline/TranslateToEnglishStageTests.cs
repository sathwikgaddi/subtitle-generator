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
/// Uses a fake ILlmProvider — this test is about TranslateToEnglishStage's own DB
/// orchestration (inheriting timing from Native cues, upsert-on-retry), not about the LLM
/// itself. The real OpenAI call is covered separately by unit tests on OpenAiLlmProvider.
/// </summary>
public class TranslateToEnglishStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeLlmProvider(TranslateToEnglishResult result) : ILlmProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake-model";

        public Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
            where TResult : class
            => Task.FromResult((TResult)(object)result);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidNativeTrack_CreatesEnglishTrackInheritingTiming()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        var promptVersionId = await SeedActivePromptVersionAsync(db);
        var (videoId, nativeTrackId) = await SeedVideoWithNativeCuesAsync(db, accountId, userId);

        var result = new TranslateToEnglishResult(["Hello there, world.", "How are you?"]);
        var stage = new TranslateToEnglishStage(db, new FakeLlmProvider(result));
        await stage.ExecuteAsync(videoId, CancellationToken.None);

        var englishTrack = await db.SubtitleTracks
            .Include(t => t.Cues).ThenInclude(c => c.Words)
            .SingleAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.English);

        Assert.Equal("en", englishTrack.LanguageCode);
        Assert.Equal(SubtitleTrackStatus.Ready, englishTrack.Status);
        Assert.Equal(2, englishTrack.Cues.Count);

        var firstCue = englishTrack.Cues.Single(c => c.SequenceNumber == 1);
        Assert.Equal(0, firstCue.StartTimeMs);
        Assert.Equal(600, firstCue.EndTimeMs);
        Assert.Equal("Hello there, world.", firstCue.GeneratedText);
        Assert.Equal(3, firstCue.Words.Count);
        Assert.All(firstCue.Words, w => Assert.Null(w.StartTimeMs));

        var secondCue = englishTrack.Cues.Single(c => c.SequenceNumber == 2);
        Assert.Equal(600, secondCue.StartTimeMs);
        Assert.Equal(1200, secondCue.EndTimeMs);

        var generation = await db.AiGenerations
            .SingleAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.TranslateToEnglish);
        Assert.Equal(englishTrack.Id, generation.SubtitleTrackId);
        Assert.Equal(promptVersionId, generation.PromptVersionId);
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedTranslationCount_Throws()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        await SeedActivePromptVersionAsync(db);
        var (videoId, _) = await SeedVideoWithNativeCuesAsync(db, accountId, userId);

        // Only one translation for two native cues.
        var result = new TranslateToEnglishResult(["Just one."]);
        var stage = new TranslateToEnglishStage(db, new FakeLlmProvider(result));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => stage.ExecuteAsync(videoId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnglishTrackAlreadyExists_ReplacesCuesRatherThanDuplicating()
    {
        await using var seedDb = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(seedDb);
        await SeedActivePromptVersionAsync(seedDb);
        var (videoId, _) = await SeedVideoWithNativeCuesAsync(seedDb, accountId, userId);

        // Fresh DbContext per run, matching production (a new DI scope per job) — see
        // NativeCleanupStageTests for why reusing one context across two runs is unsafe here.
        await using (var db1 = fixture.CreateDbContext())
        {
            var firstResult = new TranslateToEnglishResult(["Hello there, world.", "How are you?"]);
            var firstStage = new TranslateToEnglishStage(db1, new FakeLlmProvider(firstResult));
            await firstStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        await using (var db2 = fixture.CreateDbContext())
        {
            var secondResult = new TranslateToEnglishResult(["Hi world.", "How's it going?"]);
            var secondStage = new TranslateToEnglishStage(db2, new FakeLlmProvider(secondResult));
            await secondStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        await using var assertDb = fixture.CreateDbContext();
        var tracks = await assertDb.SubtitleTracks
            .Where(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.English)
            .ToListAsync();
        Assert.Single(tracks);

        var cues = await assertDb.SubtitleCues
            .Where(c => c.SubtitleTrackId == tracks[0].Id)
            .OrderBy(c => c.SequenceNumber)
            .ToListAsync();
        Assert.Equal(2, cues.Count);
        Assert.Equal("Hi world.", cues[0].GeneratedText);
        Assert.Equal("How's it going?", cues[1].GeneratedText);
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
            .SingleOrDefaultAsync(p => p.Task == PromptTask.TranslateToEnglish && p.IsActive);
        if (existing is not null)
        {
            return existing.Id;
        }

        var promptVersion = new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = PromptTask.TranslateToEnglish,
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
