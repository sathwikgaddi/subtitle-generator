# Subtitle Generator

A SaaS platform for content creators that turns uploaded video into accurate,
readable subtitles — in the speaker's native language, an English
translation, or a romanized transliteration — with automatic highlighting of
the important words in every line.

This is an **AI-first product**: speech-to-text and an LLM (native cleanup,
translation, romanization, highlighting) are the core pipeline, not a
feature bolted onto a CRUD app. Every AI provider, model, and prompt
version used is recorded against the subtitles it produced — see
[docs/Architecture.md](docs/Architecture.md) §3. The architecture is also
deliberately minimal: one compute host, one database, object storage, and
pay-per-use AI APIs — no message broker, no real-time push service, no
secrets vault. See [docs/Architecture.md](docs/Architecture.md) §6 for what
was left out and why; this is built for low, occasional traffic, not scale.

> **Status:** Pre-implementation. This repository currently contains
> documentation only. No application code has been written yet — see
> [docs/Roadmap.md](docs/Roadmap.md) for what's planned and in what order.

## What it does

1. **Upload** — a creator uploads a video file.
2. **Detect language** — the platform identifies the spoken language
   automatically.
3. **Generate subtitles** — speech is transcribed into time-aligned subtitle
   cues.
4. **Choose an output** — the creator picks how the subtitles are rendered:
   - **Native language** (original script, e.g. `ఈరోజు మనం మాట్లాడుకుందాం`)
   - **English translation** (e.g. `Today let's talk.`)
   - **Romanized transliteration** (e.g. `Eeroju manam matladukundham`)
5. **Highlight key words** — important words are detected and highlighted
   automatically; creators can add or remove highlights by hand.

See [docs/ProductRequirements.md](docs/ProductRequirements.md) for full
scope and [docs/UserFlows.md](docs/UserFlows.md) for how a creator
experiences each step end to end.

## Documentation

| Document | Purpose |
|---|---|
| [docs/ProductRequirements.md](docs/ProductRequirements.md) | What we're building, for whom, and why. MVP scope, personas, functional and non-functional requirements. |
| [docs/Architecture.md](docs/Architecture.md) | System design: components, processing pipeline, Azure services, deployment topology. |
| [docs/Database.md](docs/Database.md) | PostgreSQL schema, entity-relationship diagram, table-by-table reference. |
| [docs/API.md](docs/API.md) | REST API contract: endpoints, request/response shapes, status codes. |
| [docs/UserFlows.md](docs/UserFlows.md) | Step-by-step user journeys through upload, processing, editing, and export. |
| [docs/Roadmap.md](docs/Roadmap.md) | Phased delivery plan from MVP to future capabilities. |

## Technology stack

| Layer | Choice |
|---|---|
| Frontend | Angular (SPA), served as a static build — status via polling, no push service |
| Backend API | ASP.NET Core (REST) |
| Background processing | ASP.NET Core Worker Service, polling a PostgreSQL-backed job queue |
| Database | PostgreSQL (Azure Database for PostgreSQL, Burstable tier) |
| Media processing | FFmpeg |
| Speech-to-text & language ID | Pluggable `ISpeechToTextProvider`, pay-per-use hosted API by default |
| Native cleanup, translation, romanization, highlighting | Pluggable `ILlmProvider`, one low-cost LLM behind versioned prompts |
| Object storage | Azure Blob Storage |
| Containerization | Docker (Compose; API + Worker as two containers) |
| Cloud platform | Microsoft Azure — one App Service plan, kept minimal |

Rationale for each choice, and the full list of what was deliberately left
out to keep cost down, is in
[docs/Architecture.md](docs/Architecture.md).

## Repository layout (planned)

The application code does not exist yet. Once implementation begins, the
repository is expected to follow this shape:

```
/src
  /Subtitles.Web           # Angular application
  /Subtitles.Api            # ASP.NET Core Web API
  /Subtitles.Worker         # ASP.NET Core background worker (processing pipeline)
  /Subtitles.Domain         # Shared domain models
  /Subtitles.Infrastructure # Data access, Azure service clients
/docs                       # This documentation set
/deploy                     # Docker Compose, IaC (Bicep/Terraform)
```

This layout is a plan, not a commitment — it will be revisited when
implementation starts.

## Getting started

There is nothing to run yet. Once the MVP implementation lands, this section
will cover local setup via Docker Compose (API, Worker, Angular dev server,
PostgreSQL, and an Azure Storage emulator).
