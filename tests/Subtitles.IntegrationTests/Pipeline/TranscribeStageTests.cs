using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Data;
using Subtitles.Infrastructure.Pipeline;
using Subtitles.Infrastructure.Storage;
using Subtitles.IntegrationTests.TestSupport;
using Xunit;

namespace Subtitles.IntegrationTests.Pipeline;

/// <summary>
/// Uses a fake ISpeechToTextProvider — this test is about TranscribeStage's own DB
/// orchestration (upsert-on-retry, video field updates), not about Whisper itself. The real
/// OpenAI call is covered separately by a LiveOpenAI-tagged test on the provider.
/// </summary>
public class TranscribeStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeSpeechToTextProvider(TranscriptionResult result) : ISpeechToTextProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake-model";

        public Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, CancellationToken ct)
            => Task.FromResult(result);
    }

    private static readonly TranscriptionResult SampleResult = new(
        Text: "hello world",
        LanguageCode: "en",
        LanguageConfidence: 0.97,
        Words:
        [
            new TranscriptionWord("hello", 0, 400),
            new TranscriptionWord("world", 400, 800),
        ]);

    [Fact]
    public async Task ExecuteAsync_WithNoExistingTranscript_CreatesTranscriptAndUpdatesVideo()
    {
        var storageRoot = Directory.CreateTempSubdirectory("subtitles-storage-test-");
        try
        {
            var storage = new LocalDiskVideoStorage(
                Options.Create(new LocalDiskOptions { RootPath = storageRoot.FullName }));

            await using var db = fixture.CreateDbContext();
            var (accountId, userId) = await SeedAccountAsync(db);
            var videoId = await SeedVideoWithAudioAsync(db, storage, accountId, userId);

            var stage = new TranscribeStage(db, storage, new FakeSpeechToTextProvider(SampleResult));
            await stage.ExecuteAsync(videoId, CancellationToken.None);

            var transcript = await db.Transcripts.SingleAsync(t => t.VideoId == videoId);
            Assert.Equal("hello world", transcript.RawText);
            Assert.Equal("en", transcript.LanguageCode);
            Assert.Equal(2, transcript.WordTimestamps.Count);

            var video = await db.Videos.FindAsync([videoId], CancellationToken.None);
            Assert.Equal("en", video!.DetectedLanguageCode);
            Assert.Equal(0.97m, video.DetectedLanguageConfidence);
        }
        finally
        {
            storageRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTranscriptAlreadyExists_ReplacesItRatherThanDuplicating()
    {
        var storageRoot = Directory.CreateTempSubdirectory("subtitles-storage-test-");
        try
        {
            var storage = new LocalDiskVideoStorage(
                Options.Create(new LocalDiskOptions { RootPath = storageRoot.FullName }));

            await using var db = fixture.CreateDbContext();
            var (accountId, userId) = await SeedAccountAsync(db);
            var videoId = await SeedVideoWithAudioAsync(db, storage, accountId, userId);

            var firstStage = new TranscribeStage(db, storage, new FakeSpeechToTextProvider(SampleResult));
            await firstStage.ExecuteAsync(videoId, CancellationToken.None);

            var rerunResult = SampleResult with { Text = "hello world again" };
            var secondStage = new TranscribeStage(db, storage, new FakeSpeechToTextProvider(rerunResult));
            await secondStage.ExecuteAsync(videoId, CancellationToken.None);

            var transcripts = await db.Transcripts.Where(t => t.VideoId == videoId).ToListAsync();
            Assert.Single(transcripts);
            Assert.Equal("hello world again", transcripts[0].RawText);
        }
        finally
        {
            storageRoot.Delete(recursive: true);
        }
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

    private static async Task<Guid> SeedVideoWithAudioAsync(
        SubtitlesDbContext db, LocalDiskVideoStorage storage, Guid accountId, Guid userId)
    {
        var videoId = Guid.NewGuid();

        // TranscribeStage only reads the audio via IVideoStorage.OpenReadAsync — the actual
        // bytes don't matter since the STT provider is faked, but a real file must exist at
        // AudioBlobPath for OpenReadAsync to succeed.
        await using var audioBytes = new MemoryStream([1, 2, 3, 4]);
        var audioBlobPath = await storage.SaveAsync(videoId, "audio.mp3", audioBytes, CancellationToken.None);

        db.Videos.Add(new Video
        {
            Id = videoId,
            AccountId = accountId,
            UploadedByUserId = userId,
            OriginalFileName = "input.mp4",
            BlobPath = "unused",
            AudioBlobPath = audioBlobPath,
            Status = VideoStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return videoId;
    }
}
