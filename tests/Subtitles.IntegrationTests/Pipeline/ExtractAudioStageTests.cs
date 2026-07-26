using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Subtitles.Domain;
using Subtitles.Domain.Entities;
using Subtitles.Infrastructure.Media;
using Subtitles.Infrastructure.Pipeline;
using Subtitles.Infrastructure.Storage;
using Subtitles.IntegrationTests.TestSupport;
using Xunit;

namespace Subtitles.IntegrationTests.Pipeline;

/// <summary>
/// Uses a real ffmpeg subprocess (no fake) — the whole point of this stage is the ffmpeg
/// invocation, so a mocked runner would prove nothing. CI installs ffmpeg explicitly for
/// exactly this (.github/workflows/ci.yml).
/// </summary>
public class ExtractAudioStageTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ExecuteAsync_WithRealVideo_ProducesCompressedAudioAndUpdatesVideo()
    {
        var storageRoot = Directory.CreateTempSubdirectory("subtitles-storage-test-");
        var testVideoPath = await CreateSyntheticTestVideoAsync();

        try
        {
            var storage = new LocalDiskVideoStorage(
                Options.Create(new LocalDiskOptions { RootPath = storageRoot.FullName }));
            var ffmpeg = new FfmpegRunner(NullLogger<FfmpegRunner>.Instance);

            await using var db = fixture.CreateDbContext();
            var (accountId, userId) = await SeedAccountAsync(db);

            var videoId = Guid.NewGuid();
            await using (var sourceStream = File.OpenRead(testVideoPath))
            {
                var blobPath = await storage.SaveAsync(videoId, "input.mp4", sourceStream, CancellationToken.None);

                db.Videos.Add(new Video
                {
                    Id = videoId,
                    AccountId = accountId,
                    UploadedByUserId = userId,
                    OriginalFileName = "input.mp4",
                    BlobPath = blobPath,
                    Status = VideoStatus.Processing,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }

            var stage = new ExtractAudioStage(db, storage, ffmpeg);

            await stage.ExecuteAsync(videoId, CancellationToken.None);

            var updated = await db.Videos.FindAsync([videoId], CancellationToken.None);
            Assert.NotNull(updated!.AudioBlobPath);
            Assert.True(File.Exists(updated.AudioBlobPath), $"Expected audio file at {updated.AudioBlobPath}");

            // Compressed mp3 for a couple seconds of audio should be small — a sanity check
            // that we actually compressed rather than, say, silently copying raw video bytes.
            var audioInfo = new FileInfo(updated.AudioBlobPath);
            Assert.True(audioInfo.Length is > 0 and < 200_000, $"Unexpected audio file size: {audioInfo.Length} bytes");
        }
        finally
        {
            storageRoot.Delete(recursive: true);
            File.Delete(testVideoPath);
        }
    }

    private static async Task<(Guid AccountId, Guid UserId)> SeedAccountAsync(Subtitles.Infrastructure.Data.SubtitlesDbContext db)
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

    /// <summary>Generates a tiny video+tone fixture with ffmpeg itself — self-contained, no binary checked in.</summary>
    private static async Task<string> CreateSyntheticTestVideoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"extractaudio-fixture-{Guid.NewGuid():N}.mp4");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-y -f lavfi -i \"testsrc=duration=2:size=160x120:rate=5\" " +
                        "-f lavfi -i \"sine=frequency=440:duration=2\" " +
                        $"-c:v libx264 -c:a aac -shortest \"{path}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        // ffmpeg writes progress/diagnostics to stderr regardless of exit status. Redirecting
        // the stream without ever draining it deadlocks ffmpeg once the OS pipe buffer fills —
        // it blocks on the write and never exits. Must read it, even if just discarded.
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"ffmpeg fixture generation did not exit within 30s: {stderr}");
        }

        if (process.ExitCode != 0 || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Failed to generate the synthetic test video fixture via ffmpeg: {stderr}");
        }

        return path;
    }
}
