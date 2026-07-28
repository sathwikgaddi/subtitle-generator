using System.Text;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// Fifth pipeline stage — see docs/Architecture.md §2.3. Transliterates the Native track's
/// cues cue-for-cue into Latin script (pronunciation-preserving, not a translation — see
/// docs/ProductRequirements.md §6.4). Same shape as TranslateToEnglishStage: cue timing/
/// sequence_number are inherited from the matching Native cue, not re-derived, and Word rows
/// are created with text only (no timing) for GenerateHighlights to later flag.
/// </summary>
public class RomanizeStage(SubtitlesDbContext db, ILlmProvider llm) : IPipelineStage
{
    public JobType JobType => JobType.Romanize;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var video = await db.Videos.FindAsync([videoId], ct)
            ?? throw new InvalidOperationException($"Video {videoId} not found.");

        var nativeTrack = await db.SubtitleTracks
            .Include(t => t.Cues)
            .SingleOrDefaultAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Native, ct)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no Native track — NativeCleanup must run before Romanize.");

        var nativeCues = nativeTrack.Cues.OrderBy(c => c.SequenceNumber).ToList();
        if (nativeCues.Count == 0)
        {
            throw new InvalidOperationException($"Video {videoId}'s Native track has no cues to romanize.");
        }

        var promptVersion = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.Romanize && p.IsActive, ct)
            ?? throw new InvalidOperationException(
                "No active PromptVersion for Romanize — PromptSeeder must run at Worker startup.");

        var userPrompt = BuildUserPrompt(nativeCues);
        var result = await llm.CompleteStructuredAsync<RomanizeResult>(
            new LlmStructuredRequest(promptVersion.Template, userPrompt, RomanizeResult.Schema), ct);

        if (result.Transliterations.Count != nativeCues.Count)
        {
            throw new InvalidOperationException(
                $"Romanize returned {result.Transliterations.Count} transliterations for " +
                $"{nativeCues.Count} native cues — counts must match exactly.");
        }

        var track = await db.SubtitleTracks
            .Include(t => t.Cues)
            .SingleOrDefaultAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Romanized, ct);

        var now = DateTimeOffset.UtcNow;
        if (track is null)
        {
            track = new SubtitleTrack
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                TrackType = SubtitleTrackType.Romanized,
                LanguageCode = $"{video.DetectedLanguageCode}-Latn",
                CreatedAt = now,
            };
            db.SubtitleTracks.Add(track);
        }
        else
        {
            // Re-processing replaces cues rather than appending. Deliberately not loading
            // Words here — see NativeCleanupStage for why that would double-delete against
            // Postgres's own ON DELETE CASCADE.
            db.SubtitleCues.RemoveRange(track.Cues);
            track.Cues.Clear();
        }

        track.Status = SubtitleTrackStatus.Ready;
        track.UpdatedAt = now;

        for (var i = 0; i < nativeCues.Count; i++)
        {
            var nativeCue = nativeCues[i];
            var cueEntity = new SubtitleCue
            {
                Id = Guid.NewGuid(),
                SubtitleTrackId = track.Id,
                SequenceNumber = nativeCue.SequenceNumber,
                StartTimeMs = nativeCue.StartTimeMs,
                EndTimeMs = nativeCue.EndTimeMs,
                GeneratedText = result.Transliterations[i],
                UpdatedAt = now,
            };

            foreach (var word in SplitIntoWords(result.Transliterations[i]))
            {
                cueEntity.Words.Add(word);
                db.Words.Add(word);
            }

            track.Cues.Add(cueEntity);
            db.SubtitleCues.Add(cueEntity);
        }

        var generation = await db.AiGenerations
            .SingleOrDefaultAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.Romanize, ct);
        if (generation is null)
        {
            generation = new AiGeneration { Id = Guid.NewGuid(), VideoId = videoId, Stage = GenerationStage.Romanize };
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

    private static string BuildUserPrompt(IReadOnlyList<SubtitleCue> nativeCues)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < nativeCues.Count; i++)
        {
            sb.Append(i + 1).Append(": ").AppendLine(nativeCues[i].Text);
        }
        return sb.ToString();
    }

    private static IEnumerable<Word> SplitIntoWords(string cueText)
    {
        var tokens = cueText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            yield return new Word { Id = Guid.NewGuid(), SequenceNumber = i + 1, Text = tokens[i] };
        }
    }
}
