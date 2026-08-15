using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Subtitles.Api.Auth;
using Subtitles.Api.Contracts;
using Subtitles.Domain;
using Subtitles.Domain.Storage;
using Subtitles.Infrastructure.Data;

namespace Subtitles.Api.Controllers;

/// <summary>
/// Top-level resource per docs/API.md §4 (GET /exports/{id}, not nested under /videos) —
/// creation still happens via POST /videos/{id}/export on VideosController, matching the
/// documented split between "create under a video" and "read/download by its own id."
/// </summary>
[ApiController]
[Route("api/v1/exports")]
[Authorize]
public class ExportsController(SubtitlesDbContext db, IVideoStorage storage) : ControllerBase
{
    /// <summary>
    /// DownloadUrl points at our own /api/v1/exports/{id}/download rather than the documented
    /// short-lived Blob Storage SAS URL — local-disk storage has no such mechanism, same
    /// adaptation as P1.1's upload flow.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExportResponse>> GetById(Guid id, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var export = await db.Exports.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id && e.Video.AccountId == accountId, ct);

        if (export is null)
        {
            return NotFound(ApiError.Of("export_not_found", "No export with the given id exists for this account."));
        }

        var downloadUrl = export.Status == ExportStatus.Ready ? $"/api/v1/exports/{export.Id}/download" : null;

        return Ok(new ExportResponse(
            export.Id, export.VideoId, export.SubtitleTrackType?.ToString(), export.Format.ToString(),
            export.Status.ToString(), downloadUrl, export.CreatedAt));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var accountId = User.GetAccountId();

        var export = await db.Exports.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id && e.Video.AccountId == accountId, ct);

        if (export is null || export.Status != ExportStatus.Ready || export.BlobPath is null)
        {
            return NotFound(ApiError.Of(
                "export_not_found", "No ready export with the given id exists for this account."));
        }

        var stream = await storage.OpenReadAsync(export.BlobPath, ct);
        var (contentType, extension) = export.Format switch
        {
            ExportFormat.VTT => ("text/vtt", "vtt"),
            ExportFormat.SRT => ("application/x-subrip", "srt"),
            _ => ("application/octet-stream", "bin"),
        };

        return File(stream, contentType, $"export-{export.SubtitleTrackType}.{extension}");
    }
}
