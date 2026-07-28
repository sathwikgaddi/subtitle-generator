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
/// Uses a fake ILlmProvider — this test is about GenerateHighlightsStage's own DB orchestration
/// (rewriting IsHighlightedAuto across three tracks, preserving manual overrides, flipping
/// video.Status to Ready), not about the LLM itself.
/// </summary>
public class GenerateHighlightsStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeLlmProvider(GenerateHighlightsResult result) : ILlmProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake-model";

        public Task<TResult> CompleteStructuredAsync<TResult>(LlmStructuredRequest request, CancellationToken ct)
            where TResult : class
            => Task.FromResult((TResult)(object)result);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTracks_SetsHighlightsAcrossAllThreeTracksAndMarksVideoReady()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        var promptVersionId = await SeedActivePromptVersionAsync(db);
        var videoId = await SeedVideoWithAllTracksAsync(db, accountId, userId);

        // Cue 1: Native "Namaskaram prapancham" (2 words), highlight index 0.
        var result = new GenerateHighlightsResult(
        [
            new CueHighlights(1, [0], [1], [0]),
        ]);
        var stage = new GenerateHighlightsStage(db, new FakeLlmProvider(result));
        await stage.ExecuteAsync(videoId, CancellationToken.None);

        var words = await db.Words
            .Include(w => w.Cue).ThenInclude(c => c.SubtitleTrack)
            .Where(w => w.Cue.SubtitleTrack.VideoId == videoId)
            .ToListAsync();

        var nativeWords = words.Where(w => w.Cue.SubtitleTrack.TrackType == SubtitleTrackType.Native)
            .OrderBy(w => w.SequenceNumber).ToList();
        Assert.True(nativeWords[0].IsHighlightedAuto);
        Assert.False(nativeWords[1].IsHighlightedAuto);

        var englishWords = words.Where(w => w.Cue.SubtitleTrack.TrackType == SubtitleTrackType.English)
            .OrderBy(w => w.SequenceNumber).ToList();
        Assert.False(englishWords[0].IsHighlightedAuto);
        Assert.True(englishWords[1].IsHighlightedAuto);

        var video = await db.Videos.FindAsync([videoId]);
        Assert.Equal(VideoStatus.Ready, video!.Status);

        var generation = await db.AiGenerations
            .SingleAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.GenerateHighlights);
        Assert.Null(generation.SubtitleTrackId);
        Assert.Equal(promptVersionId, generation.PromptVersionId);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesManualOverrideAndClearsStaleAutoHighlight()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        await SeedActivePromptVersionAsync(db);
        var videoId = await SeedVideoWithAllTracksAsync(db, accountId, userId);

        // A creator has manually forced word 1 (index 1) OFF on the Native track, and a
        // previous auto-highlight run had left word 0 highlighted.
        var nativeCue = await db.SubtitleCues
            .Include(c => c.Words)
            .Include(c => c.SubtitleTrack)
            .SingleAsync(c => c.SubtitleTrack.VideoId == videoId && c.SubtitleTrack.TrackType == SubtitleTrackType.Native);
        var orderedWords = nativeCue.Words.OrderBy(w => w.SequenceNumber).ToList();
        orderedWords[0].IsHighlightedAuto = true;
        orderedWords[1].IsHighlightedManualOverride = false;
        await db.SaveChangesAsync();

        // This run decides word 1 (not word 0) is now the important one.
        var result = new GenerateHighlightsResult([new CueHighlights(1, [1], [], [])]);
        var stage = new GenerateHighlightsStage(db, new FakeLlmProvider(result));
        await stage.ExecuteAsync(videoId, CancellationToken.None);

        var refreshed = await db.Words
            .Where(w => w.CueId == nativeCue.Id)
            .OrderBy(w => w.SequenceNumber)
            .ToListAsync();

        Assert.False(refreshed[0].IsHighlightedAuto); // stale auto-highlight cleared
        Assert.True(refreshed[1].IsHighlightedAuto); // new auto-highlight set
        Assert.Equal(false, refreshed[1].IsHighlightedManualOverride); // manual override untouched
    }

    [Fact]
    public async Task ExecuteAsync_WithOutOfRangeWordIndex_Throws()
    {
        await using var db = fixture.CreateDbContext();
        var (accountId, userId) = await SeedAccountAsync(db);
        await SeedActivePromptVersionAsync(db);
        var videoId = await SeedVideoWithAllTracksAsync(db, accountId, userId);

        // Native cue 1 only has 2 words (indices 0-1) — index 5 is out of range.
        var result = new GenerateHighlightsResult([new CueHighlights(1, [5], [], [])]);
        var stage = new GenerateHighlightsStage(db, new FakeLlmProvider(result));

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

    /// <summary>Idempotent, like PromptSeeder — PostgresFixture's Postgres container (and the
    /// partial-unique-active-per-task index) is shared across every [Fact] in this class.</summary>
    private static async Task<Guid> SeedActivePromptVersionAsync(SubtitlesDbContext db)
    {
        var existing = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.GenerateHighlights && p.IsActive);
        if (existing is not null)
        {
            return existing.Id;
        }

        var promptVersion = new PromptVersion
        {
            Id = Guid.NewGuid(),
            Task = PromptTask.GenerateHighlights,
            Version = 1,
            Template = "test template",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PromptVersions.Add(promptVersion);
        await db.SaveChangesAsync();
        return promptVersion.Id;
    }

    /// <summary>Seeds a video with all three tracks already populated, as they would be by the
    /// time GenerateHighlights runs for real (after NativeCleanup/TranslateToEnglish/Romanize).
    /// Each cue has 2 words, matching the FakeLlmProvider results the tests above use.</summary>
    private static async Task<Guid> SeedVideoWithAllTracksAsync(SubtitlesDbContext db, Guid accountId, Guid userId)
    {
        var videoId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

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
            CreatedAt = now,
            UpdatedAt = now,
        });

        AddTrackWithOneCue(db, videoId, SubtitleTrackType.Native, "te", "Namaskaram prapancham", now);
        AddTrackWithOneCue(db, videoId, SubtitleTrackType.English, "en", "Hello world", now);
        AddTrackWithOneCue(db, videoId, SubtitleTrackType.Romanized, "te-Latn", "Namaskaram prapancham", now);

        await db.SaveChangesAsync();
        return videoId;
    }

    private static void AddTrackWithOneCue(
        SubtitlesDbContext db, Guid videoId, SubtitleTrackType trackType, string languageCode, string cueText, DateTimeOffset now)
    {
        var track = new SubtitleTrack
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            TrackType = trackType,
            LanguageCode = languageCode,
            Status = SubtitleTrackStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SubtitleTracks.Add(track);

        var cue = new SubtitleCue
        {
            Id = Guid.NewGuid(),
            SubtitleTrackId = track.Id,
            SequenceNumber = 1,
            StartTimeMs = 0,
            EndTimeMs = 1000,
            GeneratedText = cueText,
            UpdatedAt = now,
        };
        db.SubtitleCues.Add(cue);

        var tokens = cueText.Split(' ');
        for (var i = 0; i < tokens.Length; i++)
        {
            db.Words.Add(new Word { Id = Guid.NewGuid(), CueId = cue.Id, SequenceNumber = i + 1, Text = tokens[i] });
        }
    }
}
