import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { environment } from '../environments/environment';

interface LoginResponse {
  token: string;
}

interface SessionResponse {
  email?: unknown;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly storageKey = 'arcano.auth.token';
  private verifiedToken: string | null = null;
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

  register(email: string, password: string): Observable<void> {
    return this.http
      .post<unknown>(`${environment.apiBaseUrl}/Auth/register`, { email, password })
      .pipe(map(() => undefined));
  }

  logout(): void {
    this.storage()?.removeItem(this.storageKey);
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
    this.storage()?.setItem(this.storageKey, token);
    this.verifiedToken = null;
    this.token.set(token);
    this.email.set(null);
  }

  private readStoredToken(): string | null {
    const token = this.storage()?.getItem(this.storageKey) ?? null;
    if (!token || this.hasValidExpiry(token)) {
      return token;
    }
    this.storage()?.removeItem(this.storageKey);
    return null;
  }

  private storage(): Storage | null {
    return isPlatformBrowser(this.platformId) ? window.sessionStorage : null;
  }

  private hasValidExpiry(token: string): boolean {
    const payload = token.split('.')[1];
    if (!payload) {
      return false;
    }
    try {
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const decoded = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '='));
      const bytes = Uint8Array.from(decoded, character => character.charCodeAt(0));
      const claims: unknown = JSON.parse(new TextDecoder().decode(bytes));
      if (
        typeof claims !== 'object' ||
        claims === null ||
        !('exp' in claims) ||
        typeof claims.exp !== 'number' ||
        claims.exp * 1000 <= Date.now()
      ) {
        return false;
      }
      return true;
    } catch {
      return false;
    }
  }
}
