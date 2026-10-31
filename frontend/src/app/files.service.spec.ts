import { HttpEventType, provideHttpClient, withInterceptors } from '@angular/common/http';
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
      { id, fileName: 'report.pdf', contentType: 'application/pdf', fileSize: 2048, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false, lastModifiedAtUtc: null },
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
        isStarred: false,
        lastModifiedAtUtc: null,
      }));
    let result: FileDto[] | undefined;

    service.list().subscribe(response => (result = response));

    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush(pageOf(0, 100));
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=100&take=100`).flush(pageOf(100, 3));
    expect(result?.length).toBe(103);
    expect(result?.[102].id).toBe('102');
  });

  it('should list files of a folder with the folderId parameter', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    let result: FileDto[] | undefined;

    service.list(id).subscribe(response => (result = response));

    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&folderId=${id}`).flush([]);
    expect(result).toEqual([]);
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

  it('should emit upload progress then complete on the response', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const seen: number[] = [];
    let completed = false;

    service.upload(new File(['hello'], 'hello.txt')).subscribe({ next: p => seen.push(p), complete: () => (completed = true) });

    const request = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect(request.request.reportProgress).toBe(true);
    request.event({ type: HttpEventType.UploadProgress, loaded: 25, total: 100 });
    request.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
    expect(completed).toBe(false);
    request.flush({ id });
    expect(seen).toEqual([25, 50, 100]);
    expect(completed).toBe(true);
  });

  it('should upload a file into a folder', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);

    service.upload(new File(['hello'], 'hello.txt'), id).subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect((request.request.body as FormData).get('folderId')).toBe(id);
    request.flush({ id });
  });

  it('should move a file to a folder, or to the root with a null folderId', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    let completed = 0;

    service.moveFile(id, id).subscribe({ complete: () => completed++ });
    const toFolder = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/folder`);
    expect(toFolder.request.method).toBe('PUT');
    expect(toFolder.request.body).toEqual({ folderId: id });
    toFolder.flush(null, { status: 204, statusText: 'No Content' });

    service.moveFile(id, null).subscribe({ complete: () => completed++ });
    const toRoot = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/folder`);
    expect(toRoot.request.body).toEqual({ folderId: null });
    toRoot.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(2);
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

  it('should pass the view and search query when listing', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);

    service.list(null, 'starred').subscribe();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&view=starred`).flush([]);
    service.list(null, 'all', 'rep').subscribe();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&q=rep`).flush([]);
  });

  it('should rename and star a file', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);

    service.rename(id, 'new.txt').subscribe();
    const rename = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/name`);
    expect([rename.request.method, rename.request.body]).toEqual(['PUT', { name: 'new.txt' }]);
    rename.flush(null, { status: 204, statusText: 'No Content' });

    service.star(id, true).subscribe();
    const star = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/star`);
    expect([star.request.method, star.request.body]).toEqual(['PUT', { isStarred: true }]);
    star.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should list the trash, restore, delete forever and empty the trash', () => {
    const service = TestBed.inject(FilesService);
    const http = TestBed.inject(HttpTestingController);
    const noContent = { status: 204, statusText: 'No Content' };

    service.trash().subscribe();
    expect(http.expectOne(`${environment.apiBaseUrl}/Files/trash?skip=0&take=100`).request.method).toBe('GET');
    http.match(() => true).forEach(r => r.flush([]));

    service.restore(id).subscribe();
    const restore = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/restore`);
    expect(restore.request.method).toBe('POST');
    restore.flush(null, noContent);

    service.deletePermanent(id).subscribe();
    const forever = http.expectOne(`${environment.apiBaseUrl}/Files/${id}/permanent`);
    expect(forever.request.method).toBe('DELETE');
    forever.flush(null, noContent);

    service.emptyTrash().subscribe();
    const empty = http.expectOne(`${environment.apiBaseUrl}/Files/trash`);
    expect(empty.request.method).toBe('DELETE');
    empty.flush(null, noContent);
  });

  describe('uploadChunked', () => {
    const url = `${environment.apiBaseUrl}/Uploads`;
    const sid = 'u1';
    // 25 bytes with 10-byte chunks -> parts of 10, 10, 5.
    const file = new File(['a'.repeat(25)], 'big.bin', { type: 'application/octet-stream' });

    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    const start = (): { http: HttpTestingController; seen: number[]; state: { done: boolean; error: unknown } } => {
      const http = TestBed.inject(HttpTestingController);
      const seen: number[] = [];
      const state = { done: false, error: undefined as unknown };
      TestBed.inject(FilesService).uploadChunked(file, id).subscribe({
        next: p => seen.push(p),
        error: e => (state.error = e),
        complete: () => (state.done = true),
      });
      const create = http.expectOne(url);
      expect(create.request.body).toEqual({ fileName: 'big.bin', contentType: 'application/octet-stream', fileSize: 25, folderId: id });
      create.flush({ id: sid, chunkSize: 10 });
      return { http, seen, state };
    };

    it('should upload chunks sequentially then complete', async () => {
      const { http, seen, state } = start();
      for (const [n, size] of [[1, 10], [2, 10], [3, 5]]) {
        const put = http.expectOne(`${url}/${sid}/parts/${n}`);
        expect(put.request.method).toBe('PUT');
        expect((put.request.body as Blob).size).toBe(size);
        put.flush(null, { status: 204, statusText: 'No Content' });
      }
      expect(seen).toEqual([40, 80, 99]);
      http.expectOne(`${url}/${sid}/complete`).flush({ id });
      expect(seen).toEqual([40, 80, 99, 100]);
      expect(state.done).toBe(true);
    });

    it('should retry a failed chunk and skip it when the server already stored it', async () => {
      const { http, state } = start();
      http.expectOne(`${url}/${sid}/parts/1`).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(`${url}/${sid}/parts/2`).flush(null, { status: 500, statusText: 'Server Error' });
      await vi.advanceTimersByTimeAsync(500);
      http.expectOne(`${url}/${sid}`).flush({ fileSize: 25, chunkSize: 10, uploadedParts: [1, 2] });
      // Part 2 is stored, so the next request is part 3, not a resend of 2.
      http.expectOne(`${url}/${sid}/parts/3`).flush(null, { status: 500, statusText: 'Server Error' });
      await vi.advanceTimersByTimeAsync(500);
      http.expectOne(`${url}/${sid}`).flush({ fileSize: 25, chunkSize: 10, uploadedParts: [1, 2] });
      http.expectOne(`${url}/${sid}/parts/3`).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(`${url}/${sid}/complete`).flush({ id });
      expect(state.done).toBe(true);
    });

    it('should abort the session when a chunk keeps failing', async () => {
      const { http, state } = start();
      http.expectOne(`${url}/${sid}/parts/1`).flush(null, { status: 500, statusText: 'Server Error' });
      for (let i = 0; i < 3; i++) {
        await vi.advanceTimersByTimeAsync(500);
        http.expectOne(`${url}/${sid}`).flush({ fileSize: 25, chunkSize: 10, uploadedParts: [] });
        http.expectOne(`${url}/${sid}/parts/1`).flush(null, { status: 500, statusText: 'Server Error' });
      }
      const abort = http.expectOne(`${url}/${sid}`);
      expect(abort.request.method).toBe('DELETE');
      abort.flush(null, { status: 204, statusText: 'No Content' });
      expect((state.error as { status: number }).status).toBe(500);
      expect(state.done).toBe(false);
    });
  });
});
