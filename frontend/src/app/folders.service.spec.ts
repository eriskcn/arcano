import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FolderDto, FoldersService } from './folders.service';
import { environment } from '../environments/environment';

describe('FoldersService', () => {
  const id = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
  const parentId = '7c9e6679-7425-40de-944b-e07fc1f90ae7';
  const folder: FolderDto = { id, name: 'Reports', parentId, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should list root folders when no parent is given', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let result: FolderDto[] | undefined;

    service.list().subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders`);
    expect(request.request.method).toBe('GET');
    request.flush([folder]);
    expect(result).toEqual([folder]);
  });

  it('should list child folders of a parent folder', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let result: FolderDto[] | undefined;

    service.list(parentId).subscribe(response => (result = response));

    http.expectOne(`${environment.apiBaseUrl}/Folders?parentId=${parentId}`).flush([]);
    expect(result).toEqual([]);
  });

  it('should create a folder with its name and parent', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let result: FolderDto | undefined;

    service.create('Reports', parentId).subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Reports', parentId });
    request.flush(folder);
    expect(result).toEqual(folder);
  });

  it('should create a root folder with a null parent', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);

    service.create('Reports', null).subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders`);
    expect(request.request.body).toEqual({ name: 'Reports', parentId: null });
    request.flush({ ...folder, parentId: null });
  });

  it('should star a folder with a PUT', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);

    service.star(id, true).subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders/${id}/star`);
    expect([request.request.method, request.request.body]).toEqual(['PUT', { isStarred: true }]);
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should rename or move a folder with a PUT', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let completed = false;

    service.update(id, 'Archive', parentId).subscribe({ complete: () => (completed = true) });

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders/${id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Archive', parentId });
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(completed).toBe(true);
  });

  it('should delete a folder by id', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let completed = false;

    service.delete(id).subscribe({ complete: () => (completed = true) });

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders/${id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(completed).toBe(true);
  });

  it('should download a folder as a zip blob', () => {
    const service = TestBed.inject(FoldersService);
    const http = TestBed.inject(HttpTestingController);
    let result: Blob | undefined;

    service.download(id).subscribe(response => (result = response));

    const request = http.expectOne(`${environment.apiBaseUrl}/Folders/${id}/download`);
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    const blob = new Blob(['zip']);
    request.flush(blob);
    expect(result).toBe(blob);
  });
});
