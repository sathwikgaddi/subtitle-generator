import { Component, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap, takeWhile, timer } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  VideoApi,
  VideoDetail as VideoDetailModel,
  TrackStatusSummary,
  SubtitleTrackResponse,
  SubtitleCueResponse,
} from '../video-api';
import { extractApiErrorMessage } from '../../core/api/api-error';

const POLL_INTERVAL_MS = 4000;
const TERMINAL_STATUSES = new Set(['Ready', 'Failed']);

/** Tab order matches UserFlows.md — native first (what the creator actually spoke), then the
 * two derived outputs. */
const TRACK_TYPES: TrackStatusSummary['trackType'][] = ['Native', 'English', 'Romanized'];

@Component({
  selector: 'app-video-detail',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    MatTooltipModule,
  ],
  templateUrl: './video-detail.html',
  styleUrl: './video-detail.scss',
})
export class VideoDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly videoApi = inject(VideoApi);
  private readonly videoId = this.route.snapshot.paramMap.get('id')!;

  readonly trackTypes = TRACK_TYPES;

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly video = signal<VideoDetailModel | null>(null);

  readonly selectedIndex = signal(0);

  // One entry per track type, populated lazily the first time its tab is active while Ready;
  // cached after that so re-selecting a tab doesn't re-fetch.
  private readonly subtitlesByType = signal<Partial<Record<TrackStatusSummary['trackType'], SubtitleTrackResponse>>>({});
  private readonly loadingTypes = new Set<TrackStatusSummary['trackType']>();
  readonly subtitlesLoading = signal<TrackStatusSummary['trackType'] | null>(null);
  readonly subtitlesError = signal<string | null>(null);

  // Cue text editing — only one cue editable at a time, across any track.
  readonly editingCueId = signal<string | null>(null);
  readonly editBuffer = signal('');
  readonly savingCueId = signal<string | null>(null);
  readonly cueEditError = signal<string | null>(null);

  readonly savingWordId = signal<string | null>(null);

  constructor() {
    // Per docs/API.md §2: GET /videos/{id} is the only status mechanism (no push channel) —
    // poll on an interval while the video isn't in a terminal state yet, same as the SPA's
    // documented polling pattern.
    timer(0, POLL_INTERVAL_MS)
      .pipe(
        switchMap(() => this.videoApi.getById(this.videoId)),
        takeWhile((v) => !TERMINAL_STATUSES.has(v.status), true),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: (video) => {
          this.video.set(video);
          this.loading.set(false);
        },
        error: (err) => {
          this.errorMessage.set(extractApiErrorMessage(err, 'Could not load this video.'));
          this.loading.set(false);
        },
      });

    // Reactive rather than event-driven: MatTabGroup doesn't emit a change event for the tab
    // that's already active by default, so loading the first (Native) tab's cues on initial
    // render needs something that reacts to state, not just to future selection events. This
    // effect re-runs whenever the active tab or the video's track statuses change, so it
    // equally covers "tab just became active" and "track just turned Ready while its tab was
    // already active."
    effect(() => {
      const type = this.trackTypes[this.selectedIndex()];
      const status = this.statusFor(type);

      if (status !== 'Ready' || this.subtitlesByType()[type] || this.loadingTypes.has(type)) {
        return;
      }

      this.loadingTypes.add(type);
      this.subtitlesLoading.set(type);
      this.subtitlesError.set(null);

      this.videoApi.getSubtitles(this.videoId, type).subscribe({
        next: (subtitles) => {
          this.subtitlesByType.update((current) => ({ ...current, [type]: subtitles }));
          this.loadingTypes.delete(type);
          this.subtitlesLoading.set(null);
        },
        error: (err) => {
          this.loadingTypes.delete(type);
          this.subtitlesError.set(extractApiErrorMessage(err, `Could not load ${type} subtitles.`));
          this.subtitlesLoading.set(null);
        },
      });
    });
  }

  subtitlesFor(type: TrackStatusSummary['trackType']): SubtitleTrackResponse | undefined {
    return this.subtitlesByType()[type];
  }

  statusFor(type: TrackStatusSummary['trackType']): TrackStatusSummary['status'] | undefined {
    return this.video()?.tracks.find((t) => t.trackType === type)?.status;
  }

  formatTimestamp(ms: number): string {
    const totalSeconds = Math.floor(ms / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  }

  startEditingCue(cue: SubtitleCueResponse): void {
    this.cueEditError.set(null);
    this.editingCueId.set(cue.cueId);
    this.editBuffer.set(cue.text);
  }

  cancelEditingCue(): void {
    this.editingCueId.set(null);
    this.cueEditError.set(null);
  }

  saveEditingCue(type: TrackStatusSummary['trackType']): void {
    const cueId = this.editingCueId();
    const text = this.editBuffer().trim();
    if (!cueId || !text) {
      return;
    }

    this.savingCueId.set(cueId);
    this.cueEditError.set(null);

    this.videoApi.updateCueText(this.videoId, type, cueId, text).subscribe({
      next: (updatedCue) => {
        this.replaceCue(type, updatedCue);
        this.savingCueId.set(null);
        this.editingCueId.set(null);
      },
      error: (err) => {
        this.cueEditError.set(extractApiErrorMessage(err, 'Could not save this cue.'));
        this.savingCueId.set(null);
      },
    });
  }

  toggleWordHighlight(type: TrackStatusSummary['trackType'], cueId: string, wordId: string, currentlyHighlighted: boolean): void {
    // Avoid toggling a highlight while that same cue's text is mid-edit — the word list
    // shown during editing is the pre-edit one, so acting on it would be confusing.
    if (this.editingCueId() === cueId || this.savingWordId()) {
      return;
    }

    this.savingWordId.set(wordId);

    this.videoApi.updateWordHighlight(this.videoId, type, wordId, !currentlyHighlighted).subscribe({
      next: (updatedWord) => {
        this.replaceWord(type, cueId, wordId, updatedWord.isHighlighted);
        this.savingWordId.set(null);
      },
      error: () => {
        // A failed highlight toggle isn't worth a persistent error banner — the word simply
        // stays as it was, which is already visible feedback that nothing changed.
        this.savingWordId.set(null);
      },
    });
  }

  private replaceCue(type: TrackStatusSummary['trackType'], updatedCue: SubtitleCueResponse): void {
    this.subtitlesByType.update((current) => {
      const track = current[type];
      if (!track) {
        return current;
      }
      const cues = track.cues.map((c) => (c.cueId === updatedCue.cueId ? updatedCue : c));
      return { ...current, [type]: { ...track, cues } };
    });
  }

  private replaceWord(type: TrackStatusSummary['trackType'], cueId: string, wordId: string, isHighlighted: boolean): void {
    this.subtitlesByType.update((current) => {
      const track = current[type];
      if (!track) {
        return current;
      }
      const cues = track.cues.map((cue) => {
        if (cue.cueId !== cueId) {
          return cue;
        }
        const words = cue.words.map((w) => (w.wordId === wordId ? { ...w, isHighlighted } : w));
        return { ...cue, words };
      });
      return { ...current, [type]: { ...track, cues } };
    });
  }
}
