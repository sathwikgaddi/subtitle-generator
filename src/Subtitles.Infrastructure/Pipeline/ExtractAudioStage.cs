using Subtitles.Domain;
using Subtitles.Domain.Pipeline;
using Subtitles.Domain.Storage;
using Subtitles.Infrastructure.Data;
using Subtitles.Infrastructure.Media;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// First pipeline stage — see docs/Architecture.md §2.3. Deliberately goes through
/// IVideoStorage on both ends (read the source video, write the extracted audio) rather than
/// touching a local file path directly, even though today's only IVideoStorage
/// implementation happens to be local disk — this is what keeps the stage correct once a
/// cloud storage implementation exists later.
/// </summary>
public class ExtractAudioStage(SubtitlesDbContext db, IVideoStorage storage, FfmpegRunner ffmpeg) : IPipelineStage
{
    public JobType JobType => JobType.ExtractAudio;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var video = await db.Videos.FindAsync([videoId], ct)
            ?? throw new InvalidOperationException($"Video {videoId} not found.");

        var tempDirectory = Directory.CreateTempSubdirectory("subtitles-extractaudio-");
        try
        {
            var tempInputPath = Path.Combine(tempDirectory.FullName, "input" + Path.GetExtension(video.OriginalFileName));
            await using (var sourceStream = await storage.OpenReadAsync(video.BlobPath, ct))
            await using (var tempInputFile = File.Create(tempInputPath))
            {
                await sourceStream.CopyToAsync(tempInputFile, ct);
            }

            var tempOutputPath = Path.Combine(tempDirectory.FullName, "audio.mp3");
            await ffmpeg.ExtractCompressedAudioAsync(tempInputPath, tempOutputPath, ct);

            string audioBlobPath;
            await using (var outputStream = File.OpenRead(tempOutputPath))
            {
                audioBlobPath = await storage.SaveAsync(videoId, "audio.mp3", outputStream, ct);
            }

            video.AudioBlobPath = audioBlobPath;
            video.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
