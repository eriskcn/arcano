import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { FileDto, FilesService } from './files.service';
import { environment } from '../environments/environment';

const createToken = (): string => {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));
  return `header.${payload}.signature`;
};

describe('FilesService', () => {
  const id = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

  beforeEach(() => {
    sessionStorage.setItem('arcano.auth.token', createToken());
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    sessionStorage.clear();
  });

  it('should list files with the bearer token', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const files: FileDto[] = [
      { id, fileName: 'report.pdf', contentType: 'application/pdf', fileSize: 2048, createdAtUtc: '2026-07-22T10:00:00Z' },
    ];
    let result: FileDto[] | undefined;

    service.list().subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toMatch(/^Bearer /);
    request.flush(files);
    expect(result).toEqual(files);
  });

  it('should request the next page until a short page is returned', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const pageOf = (start: number, count: number): FileDto[] =>
      Array.from({ length: count }, (_, i) => ({
        id: String(start + i),
        fileName: `file-${start + i}.txt`,
        contentType: 'text/plain',
        fileSize: 1,
        createdAtUtc: '2026-07-22T10:00:00Z',
      }));
    let result: FileDto[] | undefined;

    service.list().subscribe(response => (result = response));

    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush(pageOf(0, 100));
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=100&take=100`).flush(pageOf(100, 3));
    expect(result?.length).toBe(103);
    expect(result?.[102].id).toBe('102');
  });

  it('should download a file as a blob', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const blob = new Blob(['hello'], { type: 'text/plain' });
    let result: Blob | undefined;

    service.download(id).subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Files/${id}`);
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(blob);
    expect(result).toBe(blob);
  });

  it('should upload a file as multipart form data', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const file = new File(['hello'], 'hello.txt', { type: 'text/plain' });
    let completed = false;

    service.upload(file).subscribe({ complete: () => (completed = true) });

    const request = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush({ id });
    expect(completed).toBe(true);
  });

  it('should delete a file by id', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    let completed = false;

    service.delete(id).subscribe({ complete: () => (completed = true) });

    const request = http.expectOne(`${environment.apiBaseUrl}/Files/${id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(completed).toBe(true);
  });
});
