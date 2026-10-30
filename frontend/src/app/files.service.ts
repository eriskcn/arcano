import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';

export interface FileDto {
  id: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class FilesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/Files`;

  list(): Observable<FileDto[]> {
    return this.http.get<FileDto[]>(this.baseUrl);
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
