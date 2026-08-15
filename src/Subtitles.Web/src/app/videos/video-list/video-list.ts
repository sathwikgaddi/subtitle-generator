import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { VideoApi, VideoSummary } from '../video-api';
import { extractApiErrorMessage } from '../../core/api/api-error';

/** Not the full set docs/ProductRequirements.md §6.2 eventually supports — just the required
 * launch languages plus a couple more, enough to prove the language-hint mechanism works.
 * Extending this list later needs no backend change; the hint is passed straight through.
 *
 * Telugu now works: with RoutedSpeechToTextProvider active, a "te" hint is handled by Sarvam,
 * which natively accepts it (unlike OpenAI's whisper-1, which rejects Telugu outright for its
 * language-forcing parameter — see OpenAiSpeechToTextProvider). It was removed from this list
 * earlier for exactly that reason and is back now that the active provider for this language
 * actually supports it. */
export const LANGUAGE_HINT_OPTIONS = [
  { code: 'te', label: 'Telugu' },
  { code: 'hi', label: 'Hindi' },
  { code: 'en', label: 'English' },
];

@Component({
  selector: 'app-video-list',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
  ],
  templateUrl: './video-list.html',
  styleUrl: './video-list.scss',
})
export class VideoList {
  private readonly videoApi = inject(VideoApi);
  private readonly router = inject(Router);

  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

  readonly languageOptions = LANGUAGE_HINT_OPTIONS;

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly videos = signal<VideoSummary[]>([]);

  // Empty string means "auto-detect" — the default, per docs/ProductRequirements.md §6.2.
  readonly languageHint = signal('');

  readonly uploading = signal(false);
  readonly uploadError = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  private reload(): void {
    this.loading.set(true);
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

  triggerFilePicker(): void {
    this.fileInput().nativeElement.click();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file later
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    this.videoApi.upload(file, this.languageHint() || undefined).subscribe({
      next: (video) => this.router.navigate(['/videos', video.videoId]),
      error: (err) => {
        this.uploadError.set(extractApiErrorMessage(err, 'Could not upload the video. Please try again.'));
        this.uploading.set(false);
      },
    });
  }
}
