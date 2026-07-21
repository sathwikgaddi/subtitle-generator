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
    public async Task<ActionResult<VideoSummary>> Upload(IFormFile file, CancellationToken ct)
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

        var video = new Video
        {
            Id = videoId,
            AccountId = User.GetAccountId(),
            UploadedByUserId = User.GetUserId(),
            OriginalFileName = file.FileName,
            BlobPath = storagePath,
            Status = VideoStatus.Processing,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Videos.Add(video);
        await db.SaveChangesAsync(ct);
        await jobQueue.EnqueueAsync(video.Id, JobType.ExtractAudio, ct);

        var summary = new VideoSummary(video.Id, video.OriginalFileName, video.Status.ToString(), null, null, video.CreatedAt);
        return CreatedAtAction(nameof(GetById), new { id = video.Id }, summary);
    }

    /// <summary>Used by Upload's Location header and, later, by polling clients per API.md §2.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoSummary>> GetById(Guid id, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var video = await db.Videos.AsNoTracking()
            .Where(v => v.Id == id && v.AccountId == accountId)
            .Select(v => new VideoSummary(v.Id, v.OriginalFileName, v.Status.ToString(), v.DurationSeconds, v.DetectedLanguageCode, v.CreatedAt))
            .FirstOrDefaultAsync(ct);

        if (video is null)
        {
            return NotFound(ApiError.Of("video_not_found", "No video with the given id exists for this account."));
        }

        return Ok(video);
    }

    /// <summary>
    /// docs/API.md §2 GET /videos. Real query against the (currently always-empty-until-
    /// upload-exists) videos table — this is the walking-skeleton proof point for M0.3:
    /// browser -> JWT-authenticated API -> Postgres, no video features built yet.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<VideoSummary>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
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
