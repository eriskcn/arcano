import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';

export interface UserShareDto {
  id: string;
  recipientEmail: string;
  createdAtUtc: string;
}

export interface SharedItemDto {
  id: string;
  kind: 'file' | 'folder';
  itemId: string;
  name: string;
  sizeBytes: number | null;
  contentType: string | null;
  ownerEmail: string;
  createdAtUtc: string;
}

export interface SharedFileDto {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  createdAtUtc: string;
}

export interface SharedChildren {
  folders: { id: string; name: string }[];
  files: SharedFileDto[];
}

export const SHARED_PAGE_SIZE = 100; // the server clamps take to 100

// Sharing with other users (owner side) and browsing what was shared with me (read-only).
@Injectable({ providedIn: 'root' })
export class UserSharesService {
  private readonly http = inject(HttpClient);
  private readonly sharesUrl = `${environment.apiBaseUrl}/UserShares`;
  private readonly sharedUrl = `${environment.apiBaseUrl}/Shared`;

  create(target: { fileId?: string; folderId?: string }, email: string): Observable<SharedItemDto & UserShareDto> {
    return this.http.post<SharedItemDto & UserShareDto>(this.sharesUrl, { ...target, email });
  }

  list(target: { fileId?: string; folderId?: string }): Observable<UserShareDto[]> {
    return this.http.get<UserShareDto[]>(this.sharesUrl, { params: { ...target } });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.sharesUrl}/${encodeURIComponent(id)}`).pipe(map(() => undefined));
  }

  incoming(): Observable<SharedItemDto[]> {
    return this.http.get<SharedItemDto[]>(this.sharedUrl);
  }

  children(shareId: string, folderId: string | null, skip = 0): Observable<SharedChildren> {
    return this.http.get<SharedChildren>(`${this.sharedUrl}/${encodeURIComponent(shareId)}/children`, {
      params: { ...(folderId ? { folderId } : {}), skip, take: SHARED_PAGE_SIZE },
    });
  }

  downloadFile(shareId: string, fileId: string): Observable<Blob> {
    return this.http.get(`${this.sharedUrl}/${encodeURIComponent(shareId)}/files/${encodeURIComponent(fileId)}/download`, { responseType: 'blob' });
  }

  downloadFolder(shareId: string, folderId: string): Observable<Blob> {
    return this.http.get(`${this.sharedUrl}/${encodeURIComponent(shareId)}/folders/${encodeURIComponent(folderId)}/download`, { responseType: 'blob' });
  }
}
