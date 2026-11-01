import { HttpClient, HttpEventType } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, catchError, concat, concatMap, filter, from, map, of, switchMap, throwError, timer } from 'rxjs';
import { environment } from '../environments/environment';

export interface FileDto {
  id: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  createdAtUtc: string;
  isStarred: boolean;
  lastModifiedAtUtc: string | null;
}

export const FILES_PAGE_SIZE = 100; // the server clamps take to 100

export type FilesView = 'all' | 'starred' | 'recent';

@Injectable({ providedIn: 'root' })
export class FilesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/Files`;

  /** One page of files, newest first; a page shorter than FILES_PAGE_SIZE is the last. */
  list(folderId?: string | null, view: FilesView = 'all', q = '', skip = 0): Observable<FileDto[]> {
    return this.fetchPage(this.baseUrl, skip, {
      ...(folderId ? { folderId } : {}),
      ...(view !== 'all' ? { view } : {}),
      ...(q ? { q } : {}),
    });
  }

  trash(skip = 0): Observable<FileDto[]> {
    return this.fetchPage(`${this.baseUrl}/trash`, skip, {});
  }

  rename(id: string, name: string): Observable<void> {
    return this.http
      .put<void>(`${this.baseUrl}/${encodeURIComponent(id)}/name`, { name })
      .pipe(map(() => undefined));
  }

  star(id: string, isStarred: boolean): Observable<void> {
    return this.http
      .put<void>(`${this.baseUrl}/${encodeURIComponent(id)}/star`, { isStarred })
      .pipe(map(() => undefined));
  }

  restore(id: string): Observable<void> {
    return this.http
      .post<void>(`${this.baseUrl}/${encodeURIComponent(id)}/restore`, {})
      .pipe(map(() => undefined));
  }

  deletePermanent(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}/permanent`)
      .pipe(map(() => undefined));
  }

  emptyTrash(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/trash`).pipe(map(() => undefined));
  }

  private fetchPage(url: string, skip: number, params: Record<string, string>): Observable<FileDto[]> {
    return this.http.get<FileDto[]>(url, { params: { skip, take: FILES_PAGE_SIZE, ...params } });
  }

  /** Emits upload progress (0-100) and completes once the server has responded. */
  upload(file: File, folderId?: string | null): Observable<number> {
    const body = new FormData();
    body.append('file', file, file.name);
    if (folderId) {
      body.append('folderId', folderId);
    }
    return this.http
      .post<unknown>(`${this.baseUrl}/upload`, body, { reportProgress: true, observe: 'events' })
      .pipe(
        filter(event => event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response),
        map(event =>
          event.type === HttpEventType.UploadProgress && event.total
            ? Math.floor((event.loaded / event.total) * 100)
            : 100,
        ),
      );
  }

  /** Chunked upload with per-chunk retry; emits progress (0-100) like `upload`. */
  uploadChunked(file: File, folderId?: string | null): Observable<number> {
    const url = `${environment.apiBaseUrl}/Uploads`;
    return this.http
      .post<{ id: string; chunkSize: number }>(url, {
        fileName: file.name,
        contentType: file.type || 'application/octet-stream', // browsers give '' for unknown extensions
        fileSize: file.size,
        folderId: folderId ?? null,
      })
      .pipe(
        switchMap(({ id, chunkSize }) => {
          const parts = Array.from({ length: Math.ceil(file.size / chunkSize) }, (_, i) => i + 1);
          const chunks = from(parts).pipe(
            concatMap(n => {
              const end = Math.min(n * chunkSize, file.size);
              return this.putPart(`${url}/${id}`, n, file.slice((n - 1) * chunkSize, end)).pipe(
                // 99 cap: 100 is emitted only once the server has completed the upload.
                map(() => Math.min(99, Math.floor((end / file.size) * 100))),
              );
            }),
          );
          return concat(chunks, this.http.post<unknown>(`${url}/${id}/complete`, {}).pipe(map(() => 100))).pipe(
            catchError(error =>
              this.http.delete(`${url}/${id}`).pipe(
                catchError(() => of(null)),
                switchMap(() => throwError(() => error)),
              ),
            ),
          );
        }),
      );
  }

  // After a failure, ask the server which parts it holds so a chunk that did land is not resent.
  private putPart(sessionUrl: string, n: number, chunk: Blob, attempt = 0): Observable<unknown> {
    return this.http
      .put(`${sessionUrl}/parts/${n}`, chunk, { headers: { 'Content-Type': 'application/octet-stream' } })
      .pipe(
        catchError(error =>
          attempt >= 3
            ? throwError(() => error)
            : timer(500).pipe(
                switchMap(() => this.http.get<{ uploadedParts: number[] }>(sessionUrl)),
                switchMap(status => (status.uploadedParts.includes(n) ? of(null) : this.putPart(sessionUrl, n, chunk, attempt + 1))),
              ),
        ),
      );
  }

  moveFile(id: string, folderId: string | null): Observable<void> {
    return this.http
      .put<void>(`${this.baseUrl}/${encodeURIComponent(id)}/folder`, { folderId })
      .pipe(map(() => undefined));
  }

  copyFile(id: string, folderId: string | null): Observable<FileDto> {
    return this.http.post<FileDto>(`${this.baseUrl}/${encodeURIComponent(id)}/copy`, { folderId });
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${encodeURIComponent(id)}`, { responseType: 'blob' });
  }

  delete(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`)
      .pipe(map(() => undefined));
  }
}
