import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AccountService, StorageDto } from './account.service';
import { environment } from '../environments/environment';

describe('AccountService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  it('should get the storage usage', () => {
    const http = TestBed.inject(HttpTestingController);
    let result: StorageDto | undefined;

    TestBed.inject(AccountService).storage().subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Account/storage`);
    expect(request.request.method).toBe('GET');
    request.flush({ usedBytes: 10, limitBytes: 100 });
    expect(result).toEqual({ usedBytes: 10, limitBytes: 100 });
    http.verify();
  });
});
