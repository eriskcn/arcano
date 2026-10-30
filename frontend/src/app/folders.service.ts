import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';

export interface FolderDto {
  id: string;
  name: string;
  parentId: string | null;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class FoldersService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/Folders`;

  list(parentId?: string | null): Observable<FolderDto[]> {
    return this.http.get<FolderDto[]>(this.baseUrl, { params: parentId ? { parentId } : {} });
  }

  create(name: string, parentId: string | null): Observable<FolderDto> {
    return this.http.post<FolderDto>(this.baseUrl, { name, parentId });
  }

  update(id: string, name: string, parentId: string | null): Observable<void> {
    return this.http
      .put<void>(`${this.baseUrl}/${encodeURIComponent(id)}`, { name, parentId })
      .pipe(map(() => undefined));
  }

  delete(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`)
      .pipe(map(() => undefined));
  }
}
