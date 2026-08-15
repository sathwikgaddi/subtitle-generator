using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Subtitles.Api.Auth;
using Subtitles.Api.Contracts;
using Subtitles.Domain;
using Subtitles.Domain.Entities;
using Subtitles.Domain.Storage;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Api.Controllers;

[ApiController]
[Route("api/v1/videos")]
[Authorize]
public class VideosController(SubtitlesDbContext db, IVideoStorage storage, JobQueueRepository jobQueue)
    : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".mp4", ".mov", ".mkv", ".webm"];

    /// <summary>
    /// Simplified from docs/API.md §2's two-step (SAS URL + complete) flow: with local-disk
    /// storage there's no external service to upload directly to, so the file comes through
    /// the API in one call. Restore the two-step flow when real cloud storage returns — see
    /// the note on this in the P1.1 discussion.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(3_221_225_472)] // ~3GB, headroom over ProductRequirements.md's 2GB target
    public async Task<ActionResult<VideoSummary>> Upload(
        IFormFile file, [FromForm] string? languageHint, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return BadRequest(ApiError.Of("invalid_request", "The uploaded file is empty."));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(ApiError.Of(
                "unsupported_format", $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", AllowedExtensions)}."));
        }

        var videoId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var readStream = file.OpenReadStream();
        var storagePath = await storage.SaveAsync(videoId, file.FileName, readStream, ct);

        try
        {
            var video = new Video
            {
                Id = videoId,
                AccountId = User.GetAccountId(),
                UploadedByUserId = User.GetUserId(),
                OriginalFileName = file.FileName,
                BlobPath = storagePath,
                // Optional — per docs/ProductRequirements.md §6.2, auto-detect is the default
                // and no manual selection is required to start processing; this only skips
                // auto-detection when a creator chooses to help it along.
                LanguageHint = string.IsNullOrWhiteSpace(languageHint) ? null : languageHint.Trim(),
                Status = VideoStatus.Processing,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // The video row and its first job must land together — if the process died
            // between two separate commits here, a video could get stuck at "Processing"
            // forever with no job ever created to move it forward. One transaction (even
            // though EnqueueAsync issues its own SaveChanges call, it's the same DbContext,
            // so it's still the same transaction until CommitAsync).
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Videos.Add(video);
            await db.SaveChangesAsync(ct);
            await jobQueue.EnqueueAsync(video.Id, JobType.ExtractAudio, ct);
            await transaction.CommitAsync(ct);

            var summary = new VideoSummary(video.Id, video.OriginalFileName, video.Status.ToString(), null, null, video.CreatedAt);
            return CreatedAtAction(nameof(GetById), new { id = video.Id }, summary);
        }
        catch
        {
            // Compensating cleanup: don't leave an orphaned file on disk if the DB portion
            // failed after the file was already written.
            await storage.DeleteAsync(storagePath, CancellationToken.None);
            throw;
        }
    }

    private static readonly SubtitleTrackType[] AllTrackTypes =
        [SubtitleTrackType.Native, SubtitleTrackType.English, SubtitleTrackType.Romanized];

    /// <summary>
    /// Used by Upload's Location header and, per docs/API.md §2, as the only status mechanism
    /// (no push channel) — the SPA polls this on an interval while status isn't yet terminal.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoDetail>> GetById(Guid id, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var video = await db.Videos.AsNoTracking()
            .Where(v => v.Id == id && v.AccountId == accountId)
            .FirstOrDefaultAsync(ct);

        if (video is null)
        {
            return NotFound(ApiError.Of("video_not_found", "No video with the given id exists for this account."));
        }

        var existingTrackStatuses = await db.SubtitleTracks.AsNoTracking()
            .Where(t => t.VideoId == id)
            .Select(t => new { t.TrackType, t.Status })
            .ToDictionaryAsync(t => t.TrackType, t => t.Status, ct);

        // Every track type is always listed, even before its row exists — a client needs to
        // know a track is going to exist (as "Pending") to render its tab, locked, ahead of
        // the stage that creates it actually running.
        var tracks = AllTrackTypes
            .Select(type => new TrackStatusSummary(
                type.ToString(),
                existingTrackStatuses.TryGetValue(type, out var status) ? status.ToString() : SubtitleTrackStatus.Pending.ToString()))
            .ToList();

        return Ok(new VideoDetail(
            video.Id, video.OriginalFileName, video.Status.ToString(), video.DurationSeconds,
            video.DetectedLanguageCode, video.DetectedLanguageConfidence, tracks, video.CreatedAt, video.UpdatedAt));
    }

    /// <summary>docs/API.md §3 GET /videos/{id}/subtitles?type={Native|English|Romanized}.</summary>
    [HttpGet("{id:guid}/subtitles")]
    public async Task<ActionResult<SubtitleTrackResponse>> GetSubtitles(
        Guid id, [FromQuery] SubtitleTrackType type, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var videoExists = await db.Videos.AsNoTracking().AnyAsync(v => v.Id == id && v.AccountId == accountId, ct);
        if (!videoExists)
        {
            return NotFound(ApiError.Of("video_not_found", "No video with the given id exists for this account."));
        }

        var track = await db.SubtitleTracks.AsNoTracking()
            .Where(t => t.VideoId == id && t.TrackType == type)
            .Select(t => new
            {
                t.Id,
                t.LanguageCode,
                t.Status,
                Cues = t.Cues
                    .OrderBy(c => c.SequenceNumber)
                    .Select(c => new SubtitleCueResponse(
                        c.Id,
                        c.SequenceNumber,
                        c.StartTimeMs,
                        c.EndTimeMs,
                        c.EditedText ?? c.GeneratedText,
                        c.EditedText != null,
                        c.Words
                            .OrderBy(w => w.SequenceNumber)
                            .Select(w => new SubtitleWordResponse(
                                w.Id, w.Text, w.IsHighlightedManualOverride ?? w.IsHighlightedAuto))
                            .ToList()))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (track is null)
        {
            return NotFound(ApiError.Of(
                "track_not_found", $"The {type} track for this video does not exist yet — check its status via GET /videos/{{id}} first."));
        }

        var generationStage = TrackTypeToGenerationStage(type);
        var generation = await db.AiGenerations.AsNoTracking()
            .Where(g => g.VideoId == id && g.Stage == generationStage)
            .Select(g => new
            {
                g.LlmProvider,
                g.LlmModel,
                PromptVersion = g.PromptVersion == null ? (int?)null : g.PromptVersion.Version,
                g.GeneratedAt,
                g.Reason,
            })
            .FirstOrDefaultAsync(ct);

        var generatedBy = generation is null
            ? null
            : new GeneratedByInfo(generation.LlmProvider, generation.LlmModel, generation.PromptVersion, generation.GeneratedAt, generation.Reason);

        return Ok(new SubtitleTrackResponse(type.ToString(), track.LanguageCode, track.Status.ToString(), generatedBy, track.Cues));
    }

    /// <summary>docs/API.md §3 GET /videos/{id}/generations.</summary>
    [HttpGet("{id:guid}/generations")]
    public async Task<ActionResult<VideoGenerationsResponse>> GetGenerations(Guid id, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var videoExists = await db.Videos.AsNoTracking().AnyAsync(v => v.Id == id && v.AccountId == accountId, ct);
        if (!videoExists)
        {
            return NotFound(ApiError.Of("video_not_found", "No video with the given id exists for this account."));
        }

        var generations = await db.AiGenerations.AsNoTracking()
            .Where(g => g.VideoId == id)
            .OrderBy(g => g.GeneratedAt)
            .Select(g => new GenerationEntry(
                g.Stage.ToString(),
                g.SpeechProvider,
                g.SpeechModel,
                g.LlmProvider,
                g.LlmModel,
                g.PromptVersion == null ? (int?)null : g.PromptVersion.Version,
                g.GeneratedAt,
                g.Reason))
            .ToListAsync(ct);

        return Ok(new VideoGenerationsResponse(id, generations));
    }

    /// <summary>docs/API.md §3 PATCH .../subtitles/{trackType}/cues/{cueId} — corrects a cue's
    /// text without touching its timing.</summary>
    [HttpPatch("{id:guid}/subtitles/{trackType}/cues/{cueId:guid}")]
    public async Task<ActionResult<SubtitleCueResponse>> UpdateCueText(
        Guid id, SubtitleTrackType trackType, Guid cueId, [FromBody] UpdateCueTextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(ApiError.Of("invalid_request", "Text must not be empty."));
        }

        var accountId = User.GetAccountId();

        var cue = await db.SubtitleCues
            .Include(c => c.Words)
            .SingleOrDefaultAsync(c =>
                c.Id == cueId &&
                c.SubtitleTrack.VideoId == id &&
                c.SubtitleTrack.TrackType == trackType &&
                c.SubtitleTrack.Video.AccountId == accountId, ct);

        if (cue is null)
        {
            return NotFound(ApiError.Of("cue_not_found", "No matching cue found for this video/track."));
        }

        cue.ApplyManualEdit(request.Text.Trim(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        var words = cue.Words
            .OrderBy(w => w.SequenceNumber)
            .Select(w => new SubtitleWordResponse(w.Id, w.Text, w.IsHighlighted))
            .ToList();

        return Ok(new SubtitleCueResponse(
            cue.Id, cue.SequenceNumber, cue.StartTimeMs, cue.EndTimeMs, cue.Text, cue.IsManuallyEdited, words));
    }

    /// <summary>docs/API.md §3 PATCH .../subtitles/{trackType}/words/{wordId}/highlight —
    /// manually forces a single word's highlight on or off.</summary>
    [HttpPatch("{id:guid}/subtitles/{trackType}/words/{wordId:guid}/highlight")]
    public async Task<ActionResult<WordHighlightResponse>> UpdateWordHighlight(
        Guid id, SubtitleTrackType trackType, Guid wordId, [FromBody] UpdateWordHighlightRequest request, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var word = await db.Words
            .SingleOrDefaultAsync(w =>
                w.Id == wordId &&
                w.Cue.SubtitleTrack.VideoId == id &&
                w.Cue.SubtitleTrack.TrackType == trackType &&
                w.Cue.SubtitleTrack.Video.AccountId == accountId, ct);

        if (word is null)
        {
            return NotFound(ApiError.Of("word_not_found", "No matching word found for this video/track."));
        }

        word.SetManualHighlight(request.Highlighted);
        await db.SaveChangesAsync(ct);

        var source = word.IsHighlightedManualOverride is not null ? "Manual" : "Auto";
        return Ok(new WordHighlightResponse(word.Id, word.Text, word.IsHighlighted, source));
    }

    private static GenerationStage TrackTypeToGenerationStage(SubtitleTrackType type) => type switch
    {
        SubtitleTrackType.Native => GenerationStage.NativeCleanup,
        SubtitleTrackType.English => GenerationStage.TranslateToEnglish,
        SubtitleTrackType.Romanized => GenerationStage.Romanize,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown subtitle track type."),
    };

    /// <summary>
    /// docs/API.md §2 GET /videos. Real query against the (currently always-empty-until-
    /// upload-exists) videos table — this is the walking-skeleton proof point for M0.3:
    /// browser -> JWT-authenticated API -> Postgres, no video features built yet.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<VideoSummary>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var accountId = User.GetAccountId();

        var query = db.Videos.AsNoTracking()
            .Where(v => v.AccountId == accountId)
            .OrderByDescending(v => v.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new VideoSummary(
                v.Id, v.OriginalFileName, v.Status.ToString(), v.DurationSeconds, v.DetectedLanguageCode, v.CreatedAt))
            .ToListAsync(ct);

        return Ok(new PagedResult<VideoSummary>(items, page, pageSize, totalCount));
    }
}
