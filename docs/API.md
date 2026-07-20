# API

REST API exposed by `Subtitles.Api`. All endpoints are under `/api/v1` and,
except auth endpoints, require a `Authorization: Bearer <jwt>` header. All
responses are JSON. This document specifies the MVP contract; it will grow
as features in [Roadmap.md](Roadmap.md) land.

## Conventions

- Timestamps are ISO 8601 UTC.
- IDs are UUIDs (strings).
- Time offsets within a video (`startTimeMs`/`endTimeMs`) are integer
  milliseconds from the start of the video.
- Errors return a standard shape:

```json
{
  "error": {
    "code": "video_not_found",
    "message": "No video with the given id exists for this account."
  }
}
```

- List endpoints are paginated with `?page=1&pageSize=20`, returning:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 42 }
```

## 1. Auth

### `POST /auth/register`

Create an account and its first user.

Request:
```json
{ "email": "creator@example.com", "password": "...", "displayName": "Asha" }
```

Response `201`:
```json
{ "userId": "...", "accountId": "...", "accessToken": "...", "refreshToken": "..." }
```

### `POST /auth/login`

Request:
```json
{ "email": "creator@example.com", "password": "..." }
```

Response `200`: same shape as register.

### `POST /auth/refresh`

Request:
```json
{ "refreshToken": "..." }
```

Response `200`: new `accessToken`/`refreshToken` pair.

## 2. Videos

### `POST /videos`

Start an upload: creates a `videos` row in `Uploaded`-pending state and
returns a short-lived SAS URL for the client to `PUT` the file directly to
Blob Storage (see [Architecture.md](Architecture.md) §2.1 — the API never
proxies video bytes).

Request:
```json
{ "fileName": "my-video.mp4", "fileSizeBytes": 104857600, "contentType": "video/mp4" }
```

Response `201`:
```json
{
  "videoId": "...",
  "uploadUrl": "https://...blob.core.windows.net/...&sig=...",
  "expiresAt": "2026-07-20T12:30:00Z"
}
```

### `POST /videos/{id}/complete`

Called by the client after the direct-to-blob upload finishes. Triggers
the processing pipeline (enqueues `ExtractAudio`).

Response `202`:
```json
{ "videoId": "...", "status": "Uploaded" }
```

### `GET /videos`

List the calling account's videos.

Response `200`:
```json
{
  "items": [
    {
      "videoId": "...",
      "originalFileName": "my-video.mp4",
      "status": "Ready",
      "durationSeconds": 187,
      "detectedLanguageCode": "te",
      "createdAt": "2026-07-19T09:00:00Z"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1
}
```

### `GET /videos/{id}`

Full detail for one video, including per-track status. This is the
**only** status mechanism — there is no push channel (see
[Architecture.md](Architecture.md) §6.3). The SPA polls this endpoint on
an interval (e.g. every 3–5 seconds) while `status` is not yet `Ready` or
`Failed`, and stops polling once it reaches a terminal state.

Response `200`:
```json
{
  "videoId": "...",
  "originalFileName": "my-video.mp4",
  "status": "Processing",
  "durationSeconds": 187,
  "detectedLanguageCode": "te",
  "detectedLanguageConfidence": 0.94,
  "tracks": [
    { "trackType": "Native", "status": "Ready" },
    { "trackType": "English", "status": "Ready" },
    { "trackType": "Romanized", "status": "Pending" }
  ],
  "createdAt": "2026-07-19T09:00:00Z",
  "updatedAt": "2026-07-19T09:04:12Z"
}
```

`status` is one of `Uploaded`, `Processing`, `Ready`, `Failed` (see
[Database.md](Database.md) §2.3); per-stage detail is not exposed here —
only per-track status, which is what the UI needs to unlock each output
tab.

### `DELETE /videos/{id}`

Soft-deletes the video and its derived data (see
[Database.md](Database.md) §3 on soft delete).

Response `204`.

## 3. Subtitles

### `GET /videos/{id}/subtitles?type={Native|English|Romanized}`

Fetch the full cue list for one output type, including word-level
highlight state.

Response `200`:
```json
{
  "trackType": "Native",
  "languageCode": "te",
  "status": "Ready",
  "generatedBy": {
    "llmProvider": "openai",
    "llmModel": "gpt-4o-mini",
    "promptVersion": 3,
    "generatedAt": "2026-07-19T09:03:40Z",
    "reason": "initial"
  },
  "cues": [
    {
      "cueId": "...",
      "sequenceNumber": 1,
      "startTimeMs": 1200,
      "endTimeMs": 3400,
      "text": "ఈరోజు మనం మాట్లాడుకుందాం",
      "isManuallyEdited": false,
      "words": [
        { "wordId": "...", "text": "ఈరోజు", "isHighlighted": true },
        { "wordId": "...", "text": "మనం", "isHighlighted": false },
        { "wordId": "...", "text": "మాట్లాడుకుందాం", "isHighlighted": true }
      ]
    }
  ]
}
```

`generatedBy` is read from `ai_generations` (see
[Database.md](Database.md) §2.11) for this track's stage
(`NativeCleanup`/`TranslateToEnglish`/`Romanize`). It covers the cue
*text* only; word highlighting is a separate stage with its own
provenance — see `GET /videos/{id}/generations` below for the full
per-stage picture, including `Transcribe` and `GenerateHighlights`.

### `GET /videos/{id}/generations`

Full AI provenance for a video, one entry per pipeline stage — the
[Architecture.md](Architecture.md) §3.4 provenance table, exposed
directly. Useful for support/debugging and for a creator-facing "how was
this generated" detail view.

Response `200`:
```json
{
  "videoId": "...",
  "generations": [
    {
      "stage": "Transcribe",
      "speechProvider": "openai",
      "speechModel": "whisper-1",
      "generatedAt": "2026-07-19T09:02:10Z",
      "reason": "initial"
    },
    {
      "stage": "NativeCleanup",
      "llmProvider": "openai",
      "llmModel": "gpt-4o-mini",
      "promptVersion": 3,
      "generatedAt": "2026-07-19T09:03:40Z",
      "reason": "initial"
    },
    {
      "stage": "TranslateToEnglish",
      "llmProvider": "openai",
      "llmModel": "gpt-4o-mini",
      "promptVersion": 2,
      "generatedAt": "2026-07-19T09:04:05Z",
      "reason": "initial"
    },
    {
      "stage": "Romanize",
      "llmProvider": "openai",
      "llmModel": "gpt-4o-mini",
      "promptVersion": 1,
      "generatedAt": "2026-07-19T09:04:12Z",
      "reason": "initial"
    },
    {
      "stage": "GenerateHighlights",
      "llmProvider": "openai",
      "llmModel": "gpt-4o-mini",
      "promptVersion": 1,
      "generatedAt": "2026-07-19T09:04:20Z",
      "reason": "initial"
    }
  ]
}
```

### `PATCH /videos/{id}/subtitles/{trackType}/cues/{cueId}`

Manually correct a cue's text. Sets `edited_text`/`is_manually_edited`
(see [Database.md](Database.md) §2.6) — does not touch timing.

Request:
```json
{ "text": "ఈరోజు మనం మాట్లాడదాం" }
```

Response `200`: the updated cue, same shape as in the list above.

### `PATCH /videos/{id}/subtitles/{trackType}/words/{wordId}/highlight`

Manually add or remove a highlight on a single word.

Request:
```json
{ "highlighted": true }
```

Response `200`:
```json
{ "wordId": "...", "text": "ఈరోజు", "isHighlighted": true, "source": "Manual" }
```

`source` is `Auto` when reflecting `is_highlighted_auto` unmodified, or
`Manual` once a manual override has been set (see
[Database.md](Database.md) §2.7).

## 4. Export

### `POST /videos/{id}/export`

Request an export. SRT/VTT exports are typically fast enough to return
synchronously; `BurnedInMp4` is always async given FFmpeg re-encode time —
both cases return an `exports` resource the client polls the same way.

Request:
```json
{ "subtitleTrackType": "English", "format": "SRT" }
```

Response `202`:
```json
{ "exportId": "...", "status": "Pending" }
```

### `GET /exports/{id}`

Response `200` (once ready):
```json
{
  "exportId": "...",
  "videoId": "...",
  "subtitleTrackType": "English",
  "format": "SRT",
  "status": "Ready",
  "downloadUrl": "https://...blob.core.windows.net/...&sig=...",
  "createdAt": "2026-07-19T09:10:00Z"
}
```

`downloadUrl` is a short-lived SAS URL, generated on read rather than
stored, so links don't outlive their intended access window.

## 5. Status updates

There is no push/streaming channel (no SignalR, no WebSocket) — see
[Architecture.md](Architecture.md) §6.3 for why. The SPA polls
`GET /videos/{id}` (§2) for video/track status and `GET /exports/{id}`
(§4) for export status, on a short interval, and stops polling once the
resource reaches a terminal state (`Ready`/`Failed`). This keeps the
client simple and avoids running a connection-oriented service for what
is, at MVP traffic, an infrequent state change.

## 6. Status codes and error codes

| HTTP status | When |
|---|---|
| `400` | Malformed request body. |
| `401` | Missing/invalid/expired JWT. |
| `403` | Authenticated but the resource belongs to a different account. |
| `404` | Resource does not exist (or belongs to a different account — MVP returns `404` rather than `403` for cross-account access to avoid confirming existence). |
| `409` | e.g. requesting an export for a track that isn't `Ready` yet. |
| `422` | Semantically invalid (e.g. `highlighted` field missing). |
| `500` | Unhandled server error. |

Common `error.code` values: `video_not_found`, `track_not_ready`,
`invalid_credentials`, `email_already_registered`, `cue_not_found`,
`word_not_found`, `export_format_unsupported`.

## 7. Not in MVP scope

Explicitly deferred (see [Roadmap.md](Roadmap.md)) and not part of this
contract yet: team/multi-user account management endpoints, billing/
subscription endpoints, public/third-party API keys, real-time push
notifications (webhook or otherwise) in place of polling, and any
endpoint for managing `prompt_versions` directly (MVP manages prompts via
migration/seed, not an admin API — see [Roadmap.md](Roadmap.md)).
