import { HttpClient, HttpEventType } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { EMPTY, Observable, catchError, concat, concatMap, expand, filter, from, map, of, reduce, switchMap, throwError, timer } from 'rxjs';
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

export type FilesView = 'all' | 'starred' | 'recent';

@Injectable({ providedIn: 'root' })
export class FilesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/Files`;

  list(folderId?: string | null, view: FilesView = 'all', q = ''): Observable<FileDto[]> {
    return this.fetchAll(this.baseUrl, {
      ...(folderId ? { folderId } : {}),
      ...(view !== 'all' ? { view } : {}),
      ...(q ? { q } : {}),
    });
  }

  trash(): Observable<FileDto[]> {
    return this.fetchAll(`${this.baseUrl}/trash`, {});
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

  private fetchAll(url: string, params: Record<string, string>): Observable<FileDto[]> {
    const pageSize = 100;
    const fetchPage = (skip: number): Observable<FileDto[]> =>
      this.http.get<FileDto[]>(url, { params: { skip, take: pageSize, ...params } });
    // expand's index counts pages fetched so far, so the next skip is (index + 1) * pageSize.
    return fetchPage(0).pipe(
      expand((page, index) => (page.length < pageSize ? EMPTY : fetchPage((index + 1) * pageSize))),
      reduce((all, page) => [...all, ...page], [] as FileDto[]),
    );
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
        contentType: file.type,
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

  download(id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${encodeURIComponent(id)}`, { responseType: 'blob' });
  }

  delete(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`)
      .pipe(map(() => undefined));
  }
}
