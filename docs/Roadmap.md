# Roadmap

Phased delivery plan. Phases are ordered by dependency and risk, not fixed
dates — each phase's exit criteria should hold before starting the next.
This roadmap will be revisited as the open questions in
[ProductRequirements.md](ProductRequirements.md) §9 get resolved.

## Phase 0 — Foundation

Not a user-facing feature phase; sets up the ability to build safely.

- Repository/solution scaffolding: `Subtitles.Web`, `Subtitles.Api`,
  `Subtitles.Worker`, shared domain/infrastructure projects.
- PostgreSQL schema migrations for the MVP tables in
  [Database.md](Database.md), including `processing_jobs` (the job queue),
  `prompt_versions`, and `ai_generations` from day one — these aren't
  Phase 2 additions, they're load-bearing for Phase 1.
- `ISpeechToTextProvider` and `ILlmProvider` interfaces
  ([Architecture.md](Architecture.md) §3.1–3.2) with one concrete
  implementation each, wired through configuration rather than hard-coded.
- Docker Compose for local dev (API, Worker, Postgres, Azurite).
- Azure resource provisioning (infrastructure as code) for the deliberately
  small footprint in [Architecture.md](Architecture.md) §6: one App
  Service (Linux, multi-container), PostgreSQL Flexible Server
  (Burstable), Blob Storage. Nothing else — no Service Bus, SignalR, Key
  Vault, Container Registry, or Application Insights (§6.1–6.7 explain
  why each is skipped).
- CI: build + test on push; CD: container build/push to GitHub Container
  Registry and deploy to the App Service.
- ASP.NET Core Identity + JWT auth wired end to end (register/login/
  refresh) with a bare Angular shell that can log in and see an empty
  video list.

**Exit criteria:** a developer can run the stack locally, and a deployed
environment exists that an authenticated user can reach, even with no
video features yet.

## Phase 1 — MVP

The scope defined in [ProductRequirements.md](ProductRequirements.md) §6.
This is the first release creators actually use.

- Direct-to-blob video upload flow (§2 in [UserFlows.md](UserFlows.md)).
- The full sequential AI pipeline
  ([Architecture.md](Architecture.md) §2.3): audio extraction →
  transcription (Speech-to-Text Provider) → LLM native cleanup → LLM
  English translation → LLM romanization → LLM highlight generation, each
  stage's output tagged with its provenance in `ai_generations`
  ([Database.md](Database.md) §2.11).
- At least one active prompt version per LLM task seeded into
  `prompt_versions` ([Database.md](Database.md) §2.10) — prompt iteration
  during Phase 1 development should already go through the versioning
  mechanism, not a hard-coded string, so the habit and the tooling exist
  before launch.
- Manual cue text correction and manual highlight add/remove per word.
- Polling-based processing status (§2 in [API.md](API.md); no push
  channel — see [Architecture.md](Architecture.md) §6.3).
- SRT and VTT export.
- Burned-in (hard-subtitled) MP4 export via FFmpeg, including highlight
  styling.
- Single-user accounts (no team features yet).

**Exit criteria:** a creator can go from upload to a downloaded, correctly
timed subtitle file in all three output types, for the required launch
languages (Telugu, Hindi at minimum), without engineering intervention.

## Phase 2 — Editor and reliability hardening

Feedback-driven improvements once real creators are using Phase 1, focused
on making the editing experience and pipeline resilience production-grade
rather than adding new top-level features.

- "Resync" action: re-translate/re-transliterate a single cue after a
  Native-text manual correction, addressing the gap called out in
  [UserFlows.md](UserFlows.md) §5.
- Per-stage retry UX surfaced clearly in the video detail page (building
  on the idempotent-stage design in [Architecture.md](Architecture.md)
  §2.4), instead of only being visible via logs.
- **Prompt-quality workflow**: a way (even an internal script, not
  necessarily a UI) to bulk-identify videos whose subtitles were generated
  under an old `prompt_version` (query pattern in
  [Architecture.md](Architecture.md) §3.4) and reprocess them with the
  active prompt, tagging the resulting `ai_generations` row with
  `reason = 'prompt_upgrade_reprocess'`. This is the payoff of the
  provenance system built in Phase 1 — Phase 2 is where it starts getting
  used, not just recorded.
- Cue splitting/merging and manual timing adjustment in the editor
  (currently timing is fully automatic in Phase 1).
- Configurable retention policy for source video/audio blobs, resolving
  the open question in [ProductRequirements.md](ProductRequirements.md)
  §9.
- Expanded language coverage based on real transcription/LLM-quality
  validation per language (not just the Speech-to-Text Provider's
  language-ID coverage, which is broader than what's launch-quality).
- Soft-delete + restore for videos (formalizing the note in
  [Database.md](Database.md) §3).
- Revisit the cost/scale trade-offs flagged in
  [Architecture.md](Architecture.md) §8 (job queue, self-hosted AI
  providers, independent API/Worker scaling) **only if** actual usage data
  says one of those triggers has been hit — not on a schedule.

## Phase 3 — Monetization and teams

Introduces the account-level capabilities the data model already
anticipates ([Database.md](Database.md) §3 notes on `accounts.plan_tier`).

- Subscription/billing integration; plan-tier enforcement (upload limits,
  export limits, etc.) on top of the existing `plan_tier` field.
- Multi-user accounts: inviting teammates to an account, shared access to
  the same videos (the `accounts`/`users` split already supports this
  structurally — this phase adds the invite/role UI and any
  per-user-role authorization the API needs).
- Usage dashboards (processing minutes, exports, storage) per account.

## Phase 4 — Advanced capabilities

Larger, more speculative features that depend on Phase 1–3 being stable
and on real usage data to prioritize correctly.

- Speaker diarization (multiple speakers labeled distinctly in subtitles).
- Custom vocabulary/pronunciation hints per account (for creator-specific
  names/jargon the Speech-to-Text Provider doesn't recognize well by
  default).
- Collaborative real-time editing (multiple teammates editing the same
  video's subtitles concurrently).
- Public API access for programmatic upload/export (third-party
  integrations), building on the REST contract in [API.md](API.md).
- Mobile app.
- Additional export targets (e.g. platform-specific caption formats,
  animated/styled highlight rendering beyond bold/color).

## Explicitly out of scope indefinitely (unless revisited)

- Live/real-time stream captioning — fundamentally different latency and
  infrastructure profile than the batch pipeline this architecture is
  built around ([Architecture.md](Architecture.md) §1).
- Dubbing/voice synthesis — a different product surface (audio generation,
  not subtitles) that would need its own requirements pass, not an
  incremental addition to this roadmap.
