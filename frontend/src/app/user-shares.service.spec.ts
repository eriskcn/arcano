import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../environments/environment';
import { UserSharesService } from './user-shares.service';

describe('UserSharesService', () => {
  const api = environment.apiBaseUrl;
  let service: UserSharesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(UserSharesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('creates a share with exactly one id and the email', () => {
    service.create({ folderId: 'f1' }, 'a@b.c').subscribe();
    const req = http.expectOne(`${api}/UserShares`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ folderId: 'f1', email: 'a@b.c' });
    req.flush({});
  });

  it('lists by file or folder and removes by id', () => {
    service.list({ fileId: 'x' }).subscribe();
    http.expectOne(r => r.url === `${api}/UserShares` && r.params.get('fileId') === 'x').flush([]);
    service.list({ folderId: 'y' }).subscribe();
    http.expectOne(r => r.url === `${api}/UserShares` && r.params.get('folderId') === 'y').flush([]);
    service.remove('s1').subscribe();
    const del = http.expectOne(`${api}/UserShares/s1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
  });

  it('reads incoming shares, children (root omits folderId) and downloads', () => {
    service.incoming().subscribe();
    http.expectOne(`${api}/Shared`).flush([]);

    service.children('s1', null).subscribe();
    const root = http.expectOne(r => r.url === `${api}/Shared/s1/children`);
    expect(root.request.params.has('folderId')).toBe(false);
    expect(root.request.params.get('take')).toBe('100');
    root.flush({ folders: [], files: [] });

    service.children('s1', 'f2', 100).subscribe();
    const sub = http.expectOne(r => r.url === `${api}/Shared/s1/children`);
    expect(sub.request.params.get('folderId')).toBe('f2');
    expect(sub.request.params.get('skip')).toBe('100');
    sub.flush({ folders: [], files: [] });

    service.downloadFile('s1', 'a').subscribe();
    expect(http.expectOne(`${api}/Shared/s1/files/a/download`).request.responseType).toBe('blob');
    let fileUrl = '';
    service.fileDownloadUrl('s1', 'a').subscribe(url => (fileUrl = url));
    const fileTicket = http.expectOne(`${api}/Shared/s1/files/a/download/ticket`);
    expect(fileTicket.request.method).toBe('POST');
    fileTicket.flush({ ticket: 't1' });
    expect(fileUrl).toBe(`${api}/Shared/s1/files/a/download?dt=t1`);
    let folderUrl = '';
    service.folderDownloadUrl('s1', 'b').subscribe(url => (folderUrl = url));
    http.expectOne(`${api}/Shared/s1/folders/b/download/ticket`).flush({ ticket: 't2' });
    expect(folderUrl).toBe(`${api}/Shared/s1/folders/b/download?dt=t2`);
  });
});
