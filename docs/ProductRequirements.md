# Product Requirements Document

## 1. Summary

Subtitle Generator is a SaaS tool for content creators. A creator uploads a
video, the platform detects the spoken language and transcribes it, and the
creator receives subtitles in the language and script they choose — native
script, English translation, or romanized transliteration — with the
important words in each line highlighted for emphasis. This document defines
what the MVP must do, who it's for, and the boundaries of the first release.

The product is **AI-first**: transcription, cleanup, translation,
romanization, and highlighting are all produced by a speech-to-text
provider and an LLM working through a versioned prompt pipeline — that
pipeline is core product surface, not internal plumbing (see §6.8 and
[Architecture.md](Architecture.md) §3). The MVP is also built for **low,
occasional traffic** and correspondingly low fixed cost — this is not a
requirement to design past (see §7 and
[Architecture.md](Architecture.md) §0).

## 2. Problem statement

Creators publishing to multi-lingual or global audiences need subtitles, but:

- Manual transcription and translation is slow and expensive.
- Existing auto-subtitle tools mostly target English and a handful of major
  languages; support for native scripts of languages like Telugu and Hindi,
  and for **romanized** renderings of them, is inconsistent or absent.
- Plain subtitles are visually flat — viewers scanning quickly benefit from
  emphasis on the words that carry the meaning of a line (names, numbers,
  key nouns/verbs), which most tools don't offer at all.

## 3. Goals

- Let a creator go from raw video to downloadable, accurate subtitles in
  under a few minutes of processing time for a typical short-form video.
- Support native-script, English-translation, and romanized output from a
  single transcription pass, without asking the creator to specify the
  source language up front.
- Automatically emphasize important words in subtitles, while giving
  creators full manual control to override the automatic choices.

### Non-goals (for MVP)

- Live/real-time captioning of streams.
- Dubbing or voice synthesis.
- Manual, from-scratch transcript editing as a primary workflow (cue text
  correction is in scope; typing a transcript from nothing is not).
- Team collaboration (multiple editors on one video), billing/subscription
  enforcement, and public API access — all deferred (see
  [Roadmap.md](Roadmap.md)).

## 4. Target users

- **Independent creators** (YouTube, Instagram, short-form video) publishing
  in a regional language who want to reach English-speaking or
  script-unfamiliar audiences.
- **Multi-lingual creators/agencies** producing content in several Indian
  languages who need consistent, fast subtitle turnaround.
- **Learners/diaspora audiences** (indirect beneficiaries) who rely on
  romanized subtitles to follow native-language content they can speak or
  understand but not read in native script.

## 5. Key terms

| Term | Definition |
|---|---|
| Native output | Subtitles rendered in the original spoken language's native script. |
| Romanized output | The native-language transcript transliterated into Latin script, preserving pronunciation, not meaning. |
| English output | A translation of the native-language transcript into English. |
| Cue | A single subtitle line with a start time, end time, and text — the basic unit of a subtitle track. |
| Highlight | A word within a cue marked for visual emphasis (e.g. bold/color) when rendered. |

## 6. MVP feature requirements

### 6.1 Video upload

- A creator can upload a video file from the Angular web app.
- Supported formats: MP4, MOV, MKV, WEBM (container-level; audio is
  extracted server-side via FFmpeg regardless of codec, within reasonable
  common codec support).
- Maximum file size and duration are configurable limits (not hard-coded
  assumptions) — MVP default target: up to 2 GB / 60 minutes.
- The creator sees upload progress and, after upload, a processing status
  that updates without manual page refresh.

### 6.2 Spoken language detection

- The platform automatically identifies the primary spoken language of the
  uploaded video's audio track — no manual language selection required to
  start processing.
- MVP language coverage is bounded by the underlying speech service's
  language identification support; Telugu and Hindi are required launch
  languages, with English and other Indian languages supported
  opportunistically based on service coverage (see
  [Architecture.md](Architecture.md) §4 for the specific service and its
  language list).
- If language detection fails or confidence is low, the creator is told and
  offered a manual language override rather than receiving a silently wrong
  transcript.

### 6.3 Subtitle generation

- The platform transcribes speech into time-aligned subtitle cues (start
  time, end time, text) covering the full duration of spoken audio.
- Cue segmentation follows natural speech pauses/punctuation rather than
  fixed-length chunks, so lines read naturally.
- Word-level timing is retained internally (not just cue-level), because
  highlighting operates at the word level (§6.5) and because it enables
  future features like karaoke-style word reveal.
- Raw speech-to-text output is cleaned up by an LLM pass (punctuation,
  casing, disfluency removal, natural cue segmentation) before it is shown
  to the creator or used as input to translation/romanization — a creator
  never sees unedited ASR output as their "Native" subtitles.

### 6.4 Output language/script selection

- After transcription, the creator can view and export subtitles in any of:
  - **Native language** — original script.
  - **English translation** — meaning-equivalent English text.
  - **Romanized language** — Latin-script transliteration of the native
    transcript (pronunciation-preserving, not a translation).
- All three outputs are derived from the single transcription pass and can
  be requested independently; generating one does not require regenerating
  the transcript.
- The creator can switch between outputs in the editor without re-uploading
  or re-processing the video.
- Cue timing (start/end times) is identical across all three output types
  for a given video — only the text differs.

### 6.5 Important-word highlighting

- After transcription, the platform automatically identifies "important"
  words per cue (e.g. content-bearing nouns, verbs, numbers, named
  entities — as opposed to function words/articles/conjunctions) and marks
  them as highlighted.
- Highlighting is computed against the **native-language transcript** and
  carried across to the English and romanized outputs for the same
  underlying words, so emphasis stays consistent across output types.
- Creators can manually add a highlight to any word or remove an
  automatically-applied highlight, per cue, in the subtitle editor.
- Manual edits are preserved: re-running or regenerating other outputs for
  the same video does not silently discard a creator's manual highlight
  changes.

### 6.6 Export

- Creators can export generated subtitles as standard subtitle files
  (SRT and WebVTT) for any of the three output types.
- Creators can export a version of the video with subtitles burned in
  (hard-coded into the video frame) via FFmpeg, honoring highlight styling.
- Exports reflect the current state of the editor, including manual
  highlight edits and any manual cue text corrections.

### 6.7 Accounts

- A creator signs up and logs in (email/password for MVP).
- Uploaded videos, transcripts, and subtitle edits are private to the
  account that created them.

### 6.8 AI generation provenance and prompt versioning

- Every AI-generated piece of output (the cleaned native transcript, the
  English translation, the romanized transliteration, and the highlight
  selections) records: the speech-to-text provider and model (where
  applicable), the LLM provider and model, the prompt version used, a
  generation timestamp, and a reason (initial generation, manual
  regeneration, or reprocessing after a prompt upgrade).
- Prompts are versioned, not hard-coded — improving a prompt creates a new
  version rather than silently changing behavior for content already
  generated under the old one.
- This exists so that when prompt or model quality improves, it's
  possible to identify exactly which previously-generated subtitles were
  produced under the old prompt/model and are candidates for
  regeneration, without guessing. See
  [Architecture.md](Architecture.md) §3.3–3.4 for the mechanism.

## 7. Non-functional requirements

- **Cost:** the MVP must run at a low, largely-fixed monthly cost
  appropriate for low, occasional traffic — this product is explicitly
  not being built to absorb enterprise-scale load, and infrastructure
  choices should reflect that rather than anticipate scale that isn't
  expected. See [Architecture.md](Architecture.md) §0 and §7.
- **Accuracy:** transcription, cleanup, translation, and romanization
  quality are bounded by the underlying speech-to-text and LLM providers;
  the product does not claim perfect accuracy and must make correcting
  cue text straightforward.
- **Processing time:** end-to-end processing (upload → all three outputs
  ready) should scale roughly linearly with video duration and not require
  the creator to keep the browser tab open synchronously — status is
  polled, not blocked on.
- **Privacy:** video and transcript data belongs to the uploading account;
  no cross-account data access. Uploaded video is retained only as long as
  needed to support editing/export (retention policy defined in
  [Architecture.md](Architecture.md)).
- **Availability:** processing failures (e.g. a transient AI provider
  error) must be retryable without forcing the creator to re-upload the
  video.
- **Internationalization of the UI itself** is out of scope for MVP — the
  Angular app UI is English; only subtitle *content* is multi-lingual.

## 8. Success metrics (initial)

- Time from upload completion to first output (native subtitles) ready.
- Percentage of cues a creator edits manually post-generation (proxy for
  transcription/translation quality — lower is better, but not zero, since
  manual correction is an intended safety valve, not a failure signal by
  itself).
- Percentage of automatic highlights a creator removes vs. keeps, and
  frequency of manual highlight additions (proxy for highlighting quality).

## 9. Open questions

These are explicitly unresolved and should be revisited before/during
implementation, not silently decided by default:

- Exact language list to advertise as "supported" at MVP launch, pending
  validation of transcription quality per language, not just service
  language-ID coverage.
- Retention period for source video files after processing completes.
- Whether English is itself a selectable "native" source language (i.e. can
  a creator upload English content and only get native + highlights, with
  translation output disabled or reduced to a no-op).
- Pricing/plan model — deferred; MVP has no billing enforcement, but the
  data model should not preclude adding it later (see
  [Database.md](Database.md)).
