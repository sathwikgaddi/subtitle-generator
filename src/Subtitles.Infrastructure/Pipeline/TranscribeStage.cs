using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Pipeline;
using Subtitles.Domain.Storage;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// Second pipeline stage — see docs/Architecture.md §2.3. Produces the raw transcript that
/// NativeCleanup turns into the actual Native subtitle track; this stage's own output is
/// intentionally never shown to the creator directly.
/// </summary>
public class TranscribeStage(SubtitlesDbContext db, IVideoStorage storage, ISpeechToTextProvider stt) : IPipelineStage
{
    public JobType JobType => JobType.Transcribe;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var video = await db.Videos.FindAsync([videoId], ct)
            ?? throw new InvalidOperationException($"Video {videoId} not found.");

        if (video.AudioBlobPath is null)
        {
            throw new InvalidOperationException(
                $"Video {videoId} has no AudioBlobPath — ExtractAudio must run before Transcribe.");
        }

        await using var audioStream = await storage.OpenReadAsync(video.AudioBlobPath, ct);
        var result = await stt.TranscribeAsync(audioStream, "audio.mp3", ct);

        var wordTimestamps = result.Words
            .Select(w => new WordTimestamp(w.Text, w.StartMs, w.EndMs))
            .ToList();

        var transcript = await db.Transcripts.SingleOrDefaultAsync(t => t.VideoId == videoId, ct);
        if (transcript is null)
        {
            transcript = new Transcript { Id = Guid.NewGuid(), VideoId = videoId };
            db.Transcripts.Add(transcript);
        }

        transcript.LanguageCode = result.LanguageCode;
        transcript.RawText = result.Text;
        transcript.WordTimestamps = wordTimestamps;
        transcript.CreatedAt = DateTimeOffset.UtcNow;

        video.DetectedLanguageCode = result.LanguageCode;
        video.DetectedLanguageConfidence = (decimal)result.LanguageConfidence;
        video.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
