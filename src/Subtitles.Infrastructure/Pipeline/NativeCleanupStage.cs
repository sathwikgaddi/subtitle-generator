using System.Text;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// Third pipeline stage — see docs/Architecture.md §2.3. Turns the raw Transcribe-stage
/// output into the Native subtitle track: cleaned text, natural cue segmentation, and
/// word-level timing for highlighting.
///
/// Per-word timing within a cue is an even split of the cue's known (real ASR-derived) time
/// range across however many cleaned words that cue has — not true forced alignment. Cleanup
/// can drop/reword filler words, so there's no reliable 1:1 mapping from cleaned words back to
/// individual original ASR words. This is accurate enough for highlight rendering but not
/// frame-precise; revisit with real forced alignment if that precision is ever needed.
/// </summary>
public class NativeCleanupStage(SubtitlesDbContext db, ILlmProvider llm) : IPipelineStage
{
    public JobType JobType => JobType.NativeCleanup;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var video = await db.Videos.FindAsync([videoId], ct)
            ?? throw new InvalidOperationException($"Video {videoId} not found.");

        var transcript = await db.Transcripts.SingleOrDefaultAsync(t => t.VideoId == videoId, ct)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no transcript — Transcribe must run before NativeCleanup.");

        var words = transcript.WordTimestamps;
        if (words.Count == 0)
        {
            throw new InvalidOperationException($"Video {videoId}'s transcript has no words to clean up.");
        }

        var promptVersion = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.NativeCleanup && p.IsActive, ct)
            ?? throw new InvalidOperationException(
                "No active PromptVersion for NativeCleanup — PromptSeeder must run at Worker startup.");

        var userPrompt = BuildUserPrompt(words);
        var result = await llm.CompleteStructuredAsync<NativeCleanupResult>(
            new LlmStructuredRequest(promptVersion.Template, userPrompt, NativeCleanupResult.Schema), ct);

        ValidateCueCoverage(result.Cues, words.Count);

        // Deliberately not .ThenInclude(c => c.Words): old cues are about to be discarded
        // wholesale, and loading their Words would make EF's change tracker try to delete
        // them explicitly in addition to Postgres's own ON DELETE CASCADE on the cue→word FK —
        // the DB cascade already removes them when the parent cue row is deleted, so EF's own
        // (redundant) delete finds 0 rows affected and throws a concurrency exception.
        var track = await db.SubtitleTracks
            .Include(t => t.Cues)
            .SingleOrDefaultAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Native, ct);

        var now = DateTimeOffset.UtcNow;
        if (track is null)
        {
            track = new SubtitleTrack
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                TrackType = SubtitleTrackType.Native,
                LanguageCode = video.DetectedLanguageCode ?? transcript.LanguageCode,
                CreatedAt = now,
            };
            db.SubtitleTracks.Add(track);
        }
        else
        {
            // Re-processing replaces cues rather than appending to them.
            db.SubtitleCues.RemoveRange(track.Cues);
            track.Cues.Clear();
        }

        track.Status = SubtitleTrackStatus.Ready;
        track.UpdatedAt = now;

        var sequenceNumber = 0;
        foreach (var cue in result.Cues)
        {
            sequenceNumber++;
            var startMs = words[cue.StartWordIndex].StartMs;
            var endMs = words[cue.EndWordIndex].EndMs;

            var cueEntity = new SubtitleCue
            {
                Id = Guid.NewGuid(),
                SubtitleTrackId = track.Id,
                SequenceNumber = sequenceNumber,
                StartTimeMs = startMs,
                EndTimeMs = endMs,
                GeneratedText = cue.Text,
                UpdatedAt = now,
            };

            foreach (var word in SplitIntoWords(cue.Text, startMs, endMs))
            {
                cueEntity.Words.Add(word);
                db.Words.Add(word);
            }

            track.Cues.Add(cueEntity);
            db.SubtitleCues.Add(cueEntity);
        }

        var generation = await db.AiGenerations
            .SingleOrDefaultAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.NativeCleanup, ct);
        if (generation is null)
        {
            generation = new AiGeneration { Id = Guid.NewGuid(), VideoId = videoId, Stage = GenerationStage.NativeCleanup };
            db.AiGenerations.Add(generation);
        }

        generation.SubtitleTrackId = track.Id;
        generation.LlmProvider = llm.ProviderName;
        generation.LlmModel = llm.ModelName;
        generation.PromptVersionId = promptVersion.Id;
        generation.Reason = GenerationReasons.Initial;
        generation.GeneratedAt = now;

        await db.SaveChangesAsync(ct);
    }

    private static string BuildUserPrompt(IReadOnlyList<WordTimestamp> words)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            sb.Append(i).Append(": ").AppendLine(words[i].Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Every original word index must be claimed by exactly one cue, in ascending contiguous
    /// order — this is what makes deriving cue timing from real ASR word timestamps valid.
    /// </summary>
    private static void ValidateCueCoverage(IReadOnlyList<NativeCleanupCue> cues, int wordCount)
    {
        if (cues.Count == 0)
        {
            throw new InvalidOperationException("NativeCleanup returned zero cues.");
        }

        var expectedNext = 0;
        foreach (var cue in cues)
        {
            if (cue.StartWordIndex != expectedNext || cue.EndWordIndex < cue.StartWordIndex || cue.EndWordIndex >= wordCount)
            {
                throw new InvalidOperationException(
                    $"NativeCleanup cue word-index range [{cue.StartWordIndex},{cue.EndWordIndex}] is invalid " +
                    $"(expected next start {expectedNext}, word count {wordCount}).");
            }

            expectedNext = cue.EndWordIndex + 1;
        }

        if (expectedNext != wordCount)
        {
            throw new InvalidOperationException(
                $"NativeCleanup cues did not cover the full transcript ({expectedNext} of {wordCount} words covered).");
        }
    }

    private static IEnumerable<Word> SplitIntoWords(string cueText, int cueStartMs, int cueEndMs)
    {
        var tokens = cueText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var durationMs = Math.Max(cueEndMs - cueStartMs, 1);

        for (var i = 0; i < tokens.Length; i++)
        {
            yield return new Word
            {
                Id = Guid.NewGuid(),
                SequenceNumber = i + 1,
                Text = tokens[i],
                StartTimeMs = cueStartMs + durationMs * i / tokens.Length,
                EndTimeMs = cueStartMs + durationMs * (i + 1) / tokens.Length,
            };
        }
    }
}
