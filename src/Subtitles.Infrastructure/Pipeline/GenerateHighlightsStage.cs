using System.Text;
using Microsoft.EntityFrameworkCore;
using Subtitles.Domain;
using Subtitles.Domain.Ai;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Pipeline;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Infrastructure.Pipeline;

/// <summary>
/// Sixth and final pipeline stage — see docs/Architecture.md §2.3. Unlike the previous three
/// LLM stages, this one doesn't create a track: it only ever rewrites Word.IsHighlightedAuto
/// on words that already exist across all three tracks (never IsHighlightedManualOverride, so
/// a creator's manual highlight choice survives a re-run — docs/Database.md §2.7). It's also
/// the terminal stage in PipelineSequence, so it's the one that flips video.Status to Ready.
/// </summary>
public class GenerateHighlightsStage(SubtitlesDbContext db, ILlmProvider llm) : IPipelineStage
{
    public JobType JobType => JobType.GenerateHighlights;

    public async Task ExecuteAsync(Guid videoId, CancellationToken ct)
    {
        var video = await db.Videos.FindAsync([videoId], ct)
            ?? throw new InvalidOperationException($"Video {videoId} not found.");

        var tracks = await db.SubtitleTracks
            .Include(t => t.Cues).ThenInclude(c => c.Words)
            .Where(t => t.VideoId == videoId)
            .ToListAsync(ct);

        var nativeTrack = tracks.SingleOrDefault(t => t.TrackType == SubtitleTrackType.Native)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no Native track — NativeCleanup must run before GenerateHighlights.");
        var englishTrack = tracks.SingleOrDefault(t => t.TrackType == SubtitleTrackType.English)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no English track — TranslateToEnglish must run before GenerateHighlights.");
        var romanizedTrack = tracks.SingleOrDefault(t => t.TrackType == SubtitleTrackType.Romanized)
            ?? throw new InvalidOperationException(
                $"Video {videoId} has no Romanized track — Romanize must run before GenerateHighlights.");

        var nativeCues = nativeTrack.Cues.OrderBy(c => c.SequenceNumber).ToList();
        var englishCues = englishTrack.Cues.OrderBy(c => c.SequenceNumber).ToList();
        var romanizedCues = romanizedTrack.Cues.OrderBy(c => c.SequenceNumber).ToList();

        if (nativeCues.Count == 0 || nativeCues.Count != englishCues.Count || nativeCues.Count != romanizedCues.Count)
        {
            throw new InvalidOperationException(
                $"Video {videoId}'s tracks have empty or mismatched cue counts " +
                $"(native={nativeCues.Count}, english={englishCues.Count}, romanized={romanizedCues.Count}).");
        }

        var promptVersion = await db.PromptVersions
            .SingleOrDefaultAsync(p => p.Task == PromptTask.GenerateHighlights && p.IsActive, ct)
            ?? throw new InvalidOperationException(
                "No active PromptVersion for GenerateHighlights — PromptSeeder must run at Worker startup.");

        var userPrompt = BuildUserPrompt(nativeCues, englishCues, romanizedCues);
        var result = await llm.CompleteStructuredAsync<GenerateHighlightsResult>(
            new LlmStructuredRequest(promptVersion.Template, userPrompt, GenerateHighlightsResult.Schema), ct);

        var resultBySequence = result.Cues.ToDictionary(c => c.SequenceNumber);

        ApplyHighlights(nativeCues, resultBySequence, r => r.HighlightedNativeWordIndices);
        ApplyHighlights(englishCues, resultBySequence, r => r.HighlightedEnglishWordIndices);
        ApplyHighlights(romanizedCues, resultBySequence, r => r.HighlightedRomanizedWordIndices);

        var now = DateTimeOffset.UtcNow;

        var generation = await db.AiGenerations
            .SingleOrDefaultAsync(g => g.VideoId == videoId && g.Stage == GenerationStage.GenerateHighlights, ct);
        if (generation is null)
        {
            generation = new AiGeneration { Id = Guid.NewGuid(), VideoId = videoId, Stage = GenerationStage.GenerateHighlights };
            db.AiGenerations.Add(generation);
        }

        // Not track-scoped — this stage touches all three tracks at once, not one.
        generation.SubtitleTrackId = null;
        generation.LlmProvider = llm.ProviderName;
        generation.LlmModel = llm.ModelName;
        generation.PromptVersionId = promptVersion.Id;
        generation.Reason = GenerationReasons.Initial;
        generation.GeneratedAt = now;

        // Terminal stage in PipelineSequence (GetNextStage(GenerateHighlights) == null) — this
        // is what actually marks the video done.
        video.Status = VideoStatus.Ready;
        video.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
    }

    private static string BuildUserPrompt(
        IReadOnlyList<SubtitleCue> nativeCues, IReadOnlyList<SubtitleCue> englishCues, IReadOnlyList<SubtitleCue> romanizedCues)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < nativeCues.Count; i++)
        {
            sb.Append("Cue ").Append(nativeCues[i].SequenceNumber).AppendLine(":");
            AppendWordList(sb, "Native", nativeCues[i].Words);
            AppendWordList(sb, "English", englishCues[i].Words);
            AppendWordList(sb, "Romanized", romanizedCues[i].Words);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void AppendWordList(StringBuilder sb, string label, ICollection<Word> words)
    {
        var ordered = words.OrderBy(w => w.SequenceNumber).ToList();
        sb.Append(label).Append(": ");
        sb.AppendLine(string.Join(' ', ordered.Select((w, idx) => $"{idx}:{w.Text}")));
    }

    private static void ApplyHighlights(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, CueHighlights> resultBySequence,
        Func<CueHighlights, IReadOnlyList<int>> selectIndices)
    {
        foreach (var cue in cues)
        {
            if (!resultBySequence.TryGetValue(cue.SequenceNumber, out var cueResult))
            {
                throw new InvalidOperationException(
                    $"GenerateHighlights result is missing cue sequence {cue.SequenceNumber}.");
            }

            var words = cue.Words.OrderBy(w => w.SequenceNumber).ToList();
            var highlightIndices = selectIndices(cueResult);

            foreach (var index in highlightIndices)
            {
                if (index < 0 || index >= words.Count)
                {
                    throw new InvalidOperationException(
                        $"GenerateHighlights returned out-of-range word index {index} for cue " +
                        $"{cue.SequenceNumber} ({words.Count} words).");
                }
            }

            var highlightSet = highlightIndices.ToHashSet();
            for (var i = 0; i < words.Count; i++)
            {
                // Always rewrite, never just set-true-and-leave — a rerun must clear a
                // previously-highlighted word that's no longer selected.
                words[i].IsHighlightedAuto = highlightSet.Contains(i);
            }
        }
    }
}
