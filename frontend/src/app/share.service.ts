import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';

export interface ShareLinkDto {
  id: string;
  fileId: string;
  token: string;
  expiresAtUtc: string | null;
  createdAtUtc: string;
}

export interface PublicShareInfo {
  fileName: string;
  contentType: string;
  fileSize: number;
}

@Injectable({ providedIn: 'root' })
export class ShareService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/Share`;

  create(fileId: string, expiresAtUtc: string | null): Observable<ShareLinkDto> {
    return this.http.post<ShareLinkDto>(this.baseUrl, { fileId, expiresAtUtc });
  }

  list(fileId: string): Observable<ShareLinkDto[]> {
    return this.http.get<ShareLinkDto[]>(this.baseUrl, { params: { fileId } });
  }

  revoke(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`)
      .pipe(map(() => undefined));
  }

  linkFor(token: string): string {
    return `${window.location.origin}/share/${encodeURIComponent(token)}`;
  }

  publicInfo(token: string): Observable<PublicShareInfo> {
    return this.http.get<PublicShareInfo>(`${this.publicUrl(token)}/info`);
  }

  /** For previews only (capped in size); saving to disk links to publicUrl. */
  publicDownload(token: string): Observable<Blob> {
    return this.http.get(this.publicUrl(token), { responseType: 'blob' });
  }

  publicUrl(token: string): string {
    return `${environment.apiBaseUrl}/Public/${encodeURIComponent(token)}`;
  }
}
