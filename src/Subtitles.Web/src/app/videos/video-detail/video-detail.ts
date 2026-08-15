import { Component, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap, takeWhile, timer } from 'rxjs';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { VideoApi, VideoDetail as VideoDetailModel, TrackStatusSummary, SubtitleTrackResponse } from '../video-api';
import { extractApiErrorMessage } from '../../core/api/api-error';

const POLL_INTERVAL_MS = 4000;
const TERMINAL_STATUSES = new Set(['Ready', 'Failed']);

/** Tab order matches UserFlows.md — native first (what the creator actually spoke), then the
 * two derived outputs. */
const TRACK_TYPES: TrackStatusSummary['trackType'][] = ['Native', 'English', 'Romanized'];

@Component({
  selector: 'app-video-detail',
  imports: [RouterLink, MatChipsModule, MatIconModule, MatProgressSpinnerModule, MatTabsModule],
  templateUrl: './video-detail.html',
  styleUrl: './video-detail.scss',
})
export class VideoDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly videoApi = inject(VideoApi);

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

  constructor() {
    const videoId = this.route.snapshot.paramMap.get('id')!;

    // Per docs/API.md §2: GET /videos/{id} is the only status mechanism (no push channel) —
    // poll on an interval while the video isn't in a terminal state yet, same as the SPA's
    // documented polling pattern.
    timer(0, POLL_INTERVAL_MS)
      .pipe(
        switchMap(() => this.videoApi.getById(videoId)),
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
      const currentVideoId = this.video()?.videoId;

      if (status !== 'Ready' || !currentVideoId || this.subtitlesByType()[type] || this.loadingTypes.has(type)) {
        return;
      }

      this.loadingTypes.add(type);
      this.subtitlesLoading.set(type);
      this.subtitlesError.set(null);

      this.videoApi.getSubtitles(currentVideoId, type).subscribe({
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
}
