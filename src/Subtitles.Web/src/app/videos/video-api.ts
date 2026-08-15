import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Matches docs/API.md §2 GET /videos. */
export interface VideoSummary {
  videoId: string;
  originalFileName: string;
  status: string;
  durationSeconds: number | null;
  detectedLanguageCode: string | null;
  createdAt: string;
}

/** Matches docs/API.md §2 GET /videos/{id} — the only status mechanism, so this is polled. */
export interface VideoDetail {
  videoId: string;
  originalFileName: string;
  status: string;
  durationSeconds: number | null;
  detectedLanguageCode: string | null;
  detectedLanguageConfidence: number | null;
  tracks: TrackStatusSummary[];
  createdAt: string;
  updatedAt: string;
}

export interface TrackStatusSummary {
  trackType: 'Native' | 'English' | 'Romanized';
  status: 'Pending' | 'Ready' | 'Failed';
}

/** Matches docs/API.md §3 GET /videos/{id}/subtitles. */
export interface SubtitleTrackResponse {
  trackType: string;
  languageCode: string;
  status: string;
  generatedBy: GeneratedByInfo | null;
  cues: SubtitleCueResponse[];
}

export interface GeneratedByInfo {
  llmProvider: string | null;
  llmModel: string | null;
  promptVersion: number | null;
  generatedAt: string;
  reason: string;
}

export interface SubtitleCueResponse {
  cueId: string;
  sequenceNumber: number;
  startTimeMs: number;
  endTimeMs: number;
  text: string;
  isManuallyEdited: boolean;
  words: SubtitleWordResponse[];
}

export interface SubtitleWordResponse {
  wordId: string;
  text: string;
  isHighlighted: boolean;
}

/** Matches docs/API.md §3 PATCH .../words/{wordId}/highlight — distinct from
 * SubtitleWordResponse since it also reports whether the highlight is a manual override. */
export interface WordHighlightResponse {
  wordId: string;
  text: string;
  isHighlighted: boolean;
  source: 'Auto' | 'Manual';
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class VideoApi {
  private readonly http = inject(HttpClient);

  list(): Observable<PagedResult<VideoSummary>> {
    return this.http.get<PagedResult<VideoSummary>>('/api/v1/videos');
  }

  getById(videoId: string): Observable<VideoDetail> {
    return this.http.get<VideoDetail>(`/api/v1/videos/${videoId}`);
  }

  getSubtitles(videoId: string, type: TrackStatusSummary['trackType']): Observable<SubtitleTrackResponse> {
    return this.http.get<SubtitleTrackResponse>(`/api/v1/videos/${videoId}/subtitles`, { params: { type } });
  }

  /**
   * languageHint (ISO-639-1, e.g. "te") is optional — omitting it leaves auto-detection in
   * place, per docs/ProductRequirements.md §6.2. Setting it skips auto-detection and biases
   * the Speech-to-Text Provider toward that language, which can materially improve accuracy
   * for lower-resource languages.
   */
  upload(file: File, languageHint?: string): Observable<VideoSummary> {
    const formData = new FormData();
    formData.append('file', file);
    if (languageHint) {
      formData.append('languageHint', languageHint);
    }
    return this.http.post<VideoSummary>('/api/v1/videos', formData);
  }

  /** Corrects a cue's text — does not touch timing. */
  updateCueText(
    videoId: string, type: TrackStatusSummary['trackType'], cueId: string, text: string,
  ): Observable<SubtitleCueResponse> {
    return this.http.patch<SubtitleCueResponse>(
      `/api/v1/videos/${videoId}/subtitles/${type}/cues/${cueId}`, { text });
  }

  /** Manually forces a single word's highlight on or off. */
  updateWordHighlight(
    videoId: string, type: TrackStatusSummary['trackType'], wordId: string, highlighted: boolean,
  ): Observable<WordHighlightResponse> {
    return this.http.patch<WordHighlightResponse>(
      `/api/v1/videos/${videoId}/subtitles/${type}/words/${wordId}/highlight`, { highlighted });
  }
}
