import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { VideoApi, VideoSummary } from '../video-api';
import { extractApiErrorMessage } from '../../core/api/api-error';

@Component({
  selector: 'app-video-list',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './video-list.html',
  styleUrl: './video-list.scss',
})
export class VideoList {
  private readonly videoApi = inject(VideoApi);

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly videos = signal<VideoSummary[]>([]);

  constructor() {
    this.videoApi.list().subscribe({
      next: (result) => {
        this.videos.set(result.items);
        this.loading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(extractApiErrorMessage(err, 'Could not load your videos.'));
        this.loading.set(false);
      },
    });
  }
}
