using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Subtitles.Api.Auth;
using Subtitles.Api.Contracts;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Api.Controllers;

[ApiController]
[Route("api/v1/videos")]
[Authorize]
public class VideosController(SubtitlesDbContext db) : ControllerBase
{
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
