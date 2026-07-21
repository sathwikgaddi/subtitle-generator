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
}
