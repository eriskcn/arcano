import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface StorageDto {
  usedBytes: number;
  limitBytes: number;
}

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);

  storage(): Observable<StorageDto> {
    return this.http.get<StorageDto>(`${environment.apiBaseUrl}/Account/storage`);
  }
}
