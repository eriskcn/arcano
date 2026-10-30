import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../environments/environment';

interface LoginResponse {
  token: string;
}

interface TokenPayload {
  exp?: unknown;
  email?: unknown;
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'?: unknown;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly storageKey = 'arcano.auth.token';
  readonly token = signal<string | null>(this.readStoredToken());
  readonly email = signal<string | null>(this.readEmail(this.token()));

  login(email: string, password: string): Observable<void> {
    return this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/Auth/login`, { email, password })
      .pipe(
        map(response => {
          if (!response || typeof response.token !== 'string' || !this.tokenPayload(response.token)) {
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
    this.token.set(null);
    this.email.set(null);
  }

  isAuthenticated(): boolean {
    const token = this.token();
    if (!token) {
      return false;
    }

    if (!this.tokenPayload(token)) {
      this.logout();
      return false;
    }
    return true;
  }

  tokenForRequest(): string | null {
    return this.isAuthenticated() ? this.token() : null;
  }

  private storeToken(token: string): void {
    if (!this.tokenPayload(token)) {
      throw new Error('The server returned an expired or invalid sign-in token.');
    }
    this.storage()?.setItem(this.storageKey, token);
    this.token.set(token);
    this.email.set(this.readEmail(token));
  }

  private readStoredToken(): string | null {
    const token = this.storage()?.getItem(this.storageKey) ?? null;
    if (!token || this.tokenPayload(token)) {
      return token;
    }
    this.storage()?.removeItem(this.storageKey);
    return null;
  }

  private storage(): Storage | null {
    return isPlatformBrowser(this.platformId) ? window.sessionStorage : null;
  }

  private tokenPayload(token: string): TokenPayload | null {
    const payload = token.split('.')[1];
    if (!payload) {
      return null;
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
        return null;
      }
      return claims as TokenPayload;
    } catch {
      return null;
    }
  }

  private readEmail(token: string | null): string | null {
    if (!token) {
      return null;
    }
    const claims = this.tokenPayload(token);
    const email = claims?.email ?? claims?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'];
    return typeof email === 'string' ? email : null;
  }
}
