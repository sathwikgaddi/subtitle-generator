using System.Text;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// Fourth pipeline stage — see docs/Architecture.md §2.3. Translates the Native track's cues
/// cue-for-cue into English. Cue timing/sequence_number are inherited directly from the
/// matching Native cue (docs/Database.md §2.6), not re-derived — the LLM only ever returns
/// text, one entry per input cue, never timestamps. Word rows exist here (text only, no
/// timing — there's no real ASR alignment for translated text) so GenerateHighlights has
/// something to propagate flags onto later.
/// </summary>
public class TranslateToEnglishStage(SubtitlesDbContext db, ILlmProvider llm) : IPipelineStage
{
    public JobType JobType => JobType.TranslateToEnglish;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var nativeTrack = await db.SubtitleTracks
            .Include(t => t.Cues)
            .SingleOrDefaultAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.Native, ct)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no Native track — NativeCleanup must run before TranslateToEnglish.");

        var nativeCues = nativeTrack.Cues.OrderBy(c => c.SequenceNumber).ToList();
        if (nativeCues.Count == 0)
        {
            throw new InvalidOperationException($"Video {videoId}'s Native track has no cues to translate.");
        }

        var promptVersion = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.TranslateToEnglish && p.IsActive, ct)
            ?? throw new InvalidOperationException(
                "No active PromptVersion for TranslateToEnglish — PromptSeeder must run at Worker startup.");

        var userPrompt = BuildUserPrompt(nativeCues);
        var result = await llm.CompleteStructuredAsync<TranslateToEnglishResult>(
            new LlmStructuredRequest(promptVersion.Template, userPrompt, TranslateToEnglishResult.Schema), ct);

        if (result.Translations.Count != nativeCues.Count)
        {
            throw new InvalidOperationException(
                $"TranslateToEnglish returned {result.Translations.Count} translations for " +
                $"{nativeCues.Count} native cues — counts must match exactly.");
        }

        var track = await db.SubtitleTracks
            .Include(t => t.Cues)
            .SingleOrDefaultAsync(t => t.VideoId == videoId && t.TrackType == SubtitleTrackType.English, ct);

        var now = DateTimeOffset.UtcNow;
        if (track is null)
        {
            track = new SubtitleTrack
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                TrackType = SubtitleTrackType.English,
                LanguageCode = "en",
                CreatedAt = now,
            };
            db.SubtitleTracks.Add(track);
        }
        else
        {
            // Re-processing replaces cues rather than appending to them. Deliberately not
            // loading Words here — Postgres's own ON DELETE CASCADE removes them when their
            // parent cue row is deleted; if EF also had them tracked it would try to delete
            // them a second time and fail (see NativeCleanupStage for the same issue).
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
                GeneratedText = result.Translations[i],
                UpdatedAt = now,
            };

            foreach (var word in SplitIntoWords(result.Translations[i]))
            {
                cueEntity.Words.Add(word);
                db.Words.Add(word);
            }

            track.Cues.Add(cueEntity);
            db.SubtitleCues.Add(cueEntity);
        }

        var generation = await db.AiGenerations
            .SingleOrDefaultAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.TranslateToEnglish, ct);
        if (generation is null)
        {
            generation = new AiGeneration { Id = Guid.NewGuid(), VideoId = videoId, Stage = GenerationStage.TranslateToEnglish };
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
