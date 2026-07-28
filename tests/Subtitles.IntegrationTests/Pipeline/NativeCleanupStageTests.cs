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
/// Uses a fake ILlmProvider — this test is about NativeCleanupStage's own DB orchestration
/// (deriving cue/word timing from real ASR timestamps, upsert-on-retry), not about the LLM
/// itself. The real OpenAI call against the actual NativeCleanup prompt/schema is covered
/// separately by a LiveOpenAI-tagged test.
/// </summary>
public class NativeCleanupStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeLlmProvider(NativeCleanupResult result) : ILlmProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake-model";

        public Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
            where TResult : class
            => Task.FromResult((TResult)(object)result);
    }

    // "hello there world how are you" — 6 words, 200ms apart, matching TwoCueResult below.
    private static readonly IReadOnlyList<WordTimestamp> SixWords =
    [
        new("hello", 0, 200),
        new("there", 200, 400),
        new("world", 400, 600),
        new("how", 600, 800),
        new("are", 800, 1000),
        new("you", 1000, 1200),
    ];

    private static readonly NativeCleanupResult TwoCueResult = new(
    [
        new NativeCleanupCue(0, 2, "Hello there, world."),
        new NativeCleanupCue(3, 5, "How are you?"),
    ]);

    [Fact]
    public async Task ExecuteAsync_WithValidTranscript_CreatesTrackCuesWordsAndGeneration()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        var promptVersionId = await SeedActivePromptVersionAsync(db);
        var videoId = await SeedVideoWithTranscriptAsync(db, accountId, userId, SixWords);

        var stage = new NativeCleanupStage(db, new FakeLlmProvider(TwoCueResult));
        await stage.ExecuteAsync(videoId, CancellationToken.None);

        var track = await db.SubtitleTracks
            .Include(t => t.Cues).ThenInclude(c => c.Words)
            .SingleAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Native);

        Assert.Equal(SubtitleTrackStatus.Ready, track.Status);
        Assert.Equal(2, track.Cues.Count);

        var firstCue = track.Cues.Single(c => c.SequenceNumber == 1);
        Assert.Equal(0, firstCue.StartTimeMs);
        Assert.Equal(600, firstCue.EndTimeMs);
        Assert.Equal("Hello there, world.", firstCue.GeneratedText);
        Assert.Equal(3, firstCue.Words.Count);

        var secondCue = track.Cues.Single(c => c.SequenceNumber == 2);
        Assert.Equal(600, secondCue.StartTimeMs);
        Assert.Equal(1200, secondCue.EndTimeMs);

        var generation = await db.AiGenerations
            .SingleAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.NativeCleanup);
        Assert.Equal(track.Id, generation.SubtitleTrackId);
        Assert.Equal("fake", generation.LlmProvider);
        Assert.Equal("fake-model", generation.LlmModel);
        Assert.Equal(promptVersionId, generation.PromptVersionId);
        Assert.Equal(GenerationReasons.Initial, generation.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTrackAlreadyExists_ReplacesCuesRatherThanDuplicating()
    {
        await using var seedDb = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(seedDb);
        await SeedActivePromptVersionAsync(seedDb);
        var videoId = await SeedVideoWithTranscriptAsync(seedDb, accountId, userId, SixWords);

        // A fresh DbContext per stage execution, same as production (PollingHostedService opens
        // a new DI scope per job) — reusing one context across two "separate" runs would leave
        // the first run's Word entities stale-tracked and conflict with Postgres's own cascade
        // delete on the second run, which isn't a real scenario, just a test artifact to avoid.
        await using (var db1 = fixture.CreateDbContext())
        {
            var firstStage = new NativeCleanupStage(db1, new FakeLlmProvider(TwoCueResult));
            await firstStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        var rerunResult = new NativeCleanupResult([new NativeCleanupCue(0, 5, "Hello there world, how are you?")]);
        await using (var db2 = fixture.CreateDbContext())
        {
            var secondStage = new NativeCleanupStage(db2, new FakeLlmProvider(rerunResult));
            await secondStage.ExecuteAsync(videoId, CancellationToken.None);
        }

        await using var assertDb = fixture.CreateDbContext();
        var tracks = await assertDb.SubtitleTracks.Where(t => t.VideoId == videoId).ToListAsync();
        Assert.Single(tracks);

        var cues = await assertDb.SubtitleCues.Where(c => c.SubtitleTrackId == tracks[0].Id).ToListAsync();
        Assert.Single(cues);
        Assert.Equal("Hello there world, how are you?", cues[0].GeneratedText);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonContiguousCueCoverage_Throws()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        await SeedActivePromptVersionAsync(db);
        var videoId = await SeedVideoWithTranscriptAsync(db, accountId, userId, SixWords);

        // Leaves a gap: index 3 is never covered.
        var badResult = new NativeCleanupResult(
        [
            new NativeCleanupCue(0, 2, "Hello there, world."),
            new NativeCleanupCue(4, 5, "are you?"),
        ]);
        var stage = new NativeCleanupStage(db, new FakeLlmProvider(badResult));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => stage.ExecuteAsync(videoId, CancellationToken.None));
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

    /// <summary>
    /// Idempotent, like PromptSeeder itself — needed because PostgresFixture's Postgres
    /// container (and prompt_versions' partial-unique-active-per-task index) is shared across
    /// every [Fact] in this class, not reset between them.
    /// </summary>
    private static async Task<Guid> SeedActivePromptVersionAsync(SubtitlesDbContext db)
    {
        var existing = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.NativeCleanup && p.IsActive);
        if (existing is not null)
        {
            return existing.Id;
        }

        var promptVersion = new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = PromptTask.NativeCleanup,
            Version = 1,
            Template = "test template",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PromptVersions.Add(promptVersion);
        await db.SaveChangesAsync();
        return promptVersion.Id;
    }

    private static async Task<Guid> SeedVideoWithTranscriptAsync(
        SubtitlesDbContext db, Guid accountId, Guid userId, IReadOnlyList<WordTimestamp> words)
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
            DetectedLanguageCode = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.Transcripts.Add(new Transcript
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            LanguageCode = "en",
            RawText = string.Join(' ', words.Select(w => w.Text)),
            WordTimestamps = words,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return videoId;
    }
}
