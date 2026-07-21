import { HttpErrorResponse } from '@angular/common/http';

/** Matches the error envelope in docs/API.md "Conventions". */
export interface ApiErrorResponse {
  error: {
    code: string;
    message: string;
  };
}

/** Pulls a human-readable message out of an API error response, with a sane fallback. */
export function extractApiErrorMessage(err: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as Partial<ApiErrorResponse> | null;
    if (body?.error?.message) {
      return body.error.message;
    }
  }
  return fallback;
}
