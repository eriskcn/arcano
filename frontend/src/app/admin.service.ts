import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface AdminUser {
  id: string;
  email: string;
  isAdmin: boolean;
  isDisabled: boolean;
  usedBytes: number;
  quotaBytes: number | null;
  effectiveQuotaBytes: number;
  createdAtUtc: string;
}

export type KeyType = 'Join' | 'Reset';

export interface AdminKey {
  id: string;
  type: KeyType;
  quotaBytes: number | null;
  userEmail: string | null;
  expiresAtUtc: string | null;
  createdAtUtc: string;
}

export interface NewKey {
  type: KeyType;
  quotaBytes?: number | null;
  userId?: string;
  expiresInDays?: number | null;
}

export interface CreatedKey {
  id: string;
  key: string;
  type: KeyType;
  expiresAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/Admin`;

  users(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>(`${this.base}/users`);
  }

  setQuota(id: string, quotaBytes: number | null): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${id}/quota`, { quotaBytes });
  }

  disable(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/users/${id}/disable`, null);
  }

  enable(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/users/${id}/enable`, null);
  }

  deleteUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/users/${id}`);
  }

  keys(): Observable<AdminKey[]> {
    return this.http.get<AdminKey[]>(`${this.base}/keys`);
  }

  createKey(request: NewKey): Observable<CreatedKey> {
    return this.http.post<CreatedKey>(`${this.base}/keys`, request);
  }

  revokeKey(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/keys/${id}`);
  }
}
