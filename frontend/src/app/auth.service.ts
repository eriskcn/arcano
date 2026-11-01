import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, share, tap } from 'rxjs';
import { environment } from '../environments/environment';

interface LoginResponse {
  token: string;
}

interface SessionResponse {
  email?: unknown;
}

// Renew when the token has less than this left.
const REFRESH_WINDOW_MS = 10 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly storageKey = 'arcano.auth.token';
  private verifiedToken: string | null = null;
  private refreshing$: Observable<void> | null = null;
  readonly token = signal<string | null>(this.readStoredToken());
  readonly email = signal<string | null>(null);

  login(email: string, password: string): Observable<void> {
    return this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/Auth/login`, { email, password })
      .pipe(
        map(response => {
          if (!response || typeof response.token !== 'string' || !this.hasValidExpiry(response.token)) {
            throw new Error('The server returned an invalid sign-in response.');
          }
          return response.token;
        }),
        tap(token => this.storeToken(token)),
        map(() => undefined),
      );
  }

  // One in-flight refresh is shared by concurrent callers.
  refresh(): Observable<void> {
    return (this.refreshing$ ??= this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/Auth/refresh`, null)
      .pipe(
        map(response => {
          if (!response || typeof response.token !== 'string' || !this.hasValidExpiry(response.token)) {
            throw new Error('The server returned an invalid refresh response.');
          }
          return response.token;
        }),
        tap(token => {
          const email = this.email();
          this.storeToken(token);
          this.email.set(email);
        }),
        map(() => undefined),
        finalize(() => (this.refreshing$ = null)),
        share(),
      ));
  }

  needsRefresh(): boolean {
    const token = this.token();
    const expiry = token ? this.expiryMs(token) : null;
    return expiry !== null && expiry > Date.now() && expiry - Date.now() < REFRESH_WINDOW_MS;
  }

  register(email: string, password: string): Observable<void> {
    return this.http
      .post<unknown>(`${environment.apiBaseUrl}/Auth/register`, { email, password })
      .pipe(map(() => undefined));
  }

  // Other sessions are revoked server-side; the returned token keeps this one signed in.
  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/Account/password`, { currentPassword, newPassword })
      .pipe(
        map(response => {
          if (!response || typeof response.token !== 'string' || !this.hasValidExpiry(response.token)) {
            throw new Error('The server returned an invalid response.');
          }
          return response.token;
        }),
        tap(token => {
          const email = this.email();
          this.storeToken(token);
          this.email.set(email);
        }),
        map(() => undefined),
      );
  }

  // Revokes every token of the user on the server, then signs out locally.
  logoutEverywhere(): Observable<void> {
    return this.http.post<void>(`${environment.apiBaseUrl}/Auth/logout`, null).pipe(
      tap(() => this.logout()),
      map(() => undefined),
    );
  }

  deleteAccount(password: string): Observable<void> {
    return this.http
      .delete<void>(`${environment.apiBaseUrl}/Account`, { body: { password } })
      .pipe(
        tap(() => this.logout()),
        map(() => undefined),
      );
  }

  logout(): void {
    this.clearStored();
    this.verifiedToken = null;
    this.token.set(null);
    this.email.set(null);
  }

  validateSession(): Observable<boolean> {
    const token = this.token();
    if (!token || !this.tokenForRequest()) {
      return of(false);
    }
    if (this.verifiedToken === token) {
      return of(true);
    }

    return this.http.get<SessionResponse>(`${environment.apiBaseUrl}/Auth/session`).pipe(
      map(session => {
        this.verifiedToken = token;
        this.email.set(typeof session?.email === 'string' ? session.email : null);
        return true;
      }),
      catchError(error => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.logout();
          return of(false);
        }
        throw error;
      }),
    );
  }

  tokenForRequest(): string | null {
    const token = this.token();
    if (!token) {
      return null;
    }
    if (!this.hasValidExpiry(token)) {
      this.logout();
      return null;
    }
    return token;
  }

  private storeToken(token: string): void {
    if (!this.hasValidExpiry(token)) {
      throw new Error('The server returned an expired or invalid sign-in token.');
    }
    try {
      this.storage()?.setItem(this.storageKey, token);
    } catch {
      // Storage blocked or full: keep the session in memory only.
    }
    this.verifiedToken = null;
    this.token.set(token);
    this.email.set(null);
  }

  private readStoredToken(): string | null {
    let token: string | null = null;
    try {
      token = this.storage()?.getItem(this.storageKey) ?? null;
      if (!token) {
        // One-time migration from the previous per-tab sessionStorage location.
        token = window.sessionStorage?.getItem(this.storageKey) ?? null;
        if (token && this.hasValidExpiry(token)) {
          this.storage()?.setItem(this.storageKey, token);
        }
        if (token) {
          window.sessionStorage.removeItem(this.storageKey);
        }
      }
    } catch {
      return token && this.hasValidExpiry(token) ? token : null;
    }
    if (!token || this.hasValidExpiry(token)) {
      return token;
    }
    this.clearStored();
    return null;
  }

  private clearStored(): void {
    for (const store of [this.storage(), isPlatformBrowser(this.platformId) ? window.sessionStorage : null]) {
      try {
        store?.removeItem(this.storageKey);
      } catch {
        // Ignore blocked storage.
      }
    }
  }

  private storage(): Storage | null {
    return isPlatformBrowser(this.platformId) ? window.localStorage : null;
  }

  private hasValidExpiry(token: string): boolean {
    const expiry = this.expiryMs(token);
    return expiry !== null && expiry > Date.now();
  }

  // Decodes the JWT payload and returns `exp` in milliseconds, or null if unreadable.
  private expiryMs(token: string): number | null {
    const payload = token.split('.')[1];
    if (!payload) {
      return null;
    }
    try {
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const decoded = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '='));
      const bytes = Uint8Array.from(decoded, character => character.charCodeAt(0));
      const claims: unknown = JSON.parse(new TextDecoder().decode(bytes));
      if (typeof claims !== 'object' || claims === null || !('exp' in claims) || typeof claims.exp !== 'number') {
        return null;
      }
      return claims.exp * 1000;
    } catch {
      return null;
    }
  }
}
