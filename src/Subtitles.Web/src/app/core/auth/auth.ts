import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap, throwError } from 'rxjs';

/** Matches docs/API.md §1 register/login/refresh response shape. */
export interface AuthResult {
  userId: string;
  accountId: string;
  accessToken: string;
  refreshToken: string;
}

const ACCESS_TOKEN_KEY = 'subtitles.accessToken';
const REFRESH_TOKEN_KEY = 'subtitles.refreshToken';
const ACCOUNT_ID_KEY = 'subtitles.accountId';

@Injectable({
  providedIn: 'root',
})
export class Auth {
  private readonly http = inject(HttpClient);

  private readonly accountIdSignal = signal<string | null>(localStorage.getItem(ACCOUNT_ID_KEY));
  readonly isAuthenticated = computed(() => this.accountIdSignal() !== null);

  get accessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  private get refreshTokenValue(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  login(email: string, password: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>('/api/v1/auth/login', { email, password })
      .pipe(tap((result) => this.setSession(result)));
  }

  register(email: string, password: string, displayName: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>('/api/v1/auth/register', { email, password, displayName })
      .pipe(tap((result) => this.setSession(result)));
  }

  /** Silent session bootstrap on app start — lets a returning creator skip logging in again. */
  refresh(): Observable<AuthResult> {
    const refreshToken = this.refreshTokenValue;
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available.'));
    }
    return this.http
      .post<AuthResult>('/api/v1/auth/refresh', { refreshToken })
      .pipe(tap((result) => this.setSession(result)));
  }

  logout(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(ACCOUNT_ID_KEY);
    this.accountIdSignal.set(null);
  }

  private setSession(result: AuthResult): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, result.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, result.refreshToken);
    localStorage.setItem(ACCOUNT_ID_KEY, result.accountId);
    this.accountIdSignal.set(result.accountId);
  }
}
