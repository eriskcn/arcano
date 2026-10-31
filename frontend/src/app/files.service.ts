import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { EMPTY, Observable, expand, map, reduce } from 'rxjs';
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

  upload(file: File, folderId?: string | null): Observable<void> {
    const body = new FormData();
    body.append('file', file, file.name);
    if (folderId) {
      body.append('folderId', folderId);
    }
    return this.http.post<unknown>(`${this.baseUrl}/upload`, body).pipe(map(() => undefined));
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
