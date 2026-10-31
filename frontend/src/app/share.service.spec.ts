import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../environments/environment';
import { ShareLinkDto, ShareService } from './share.service';

describe('ShareService', () => {
  const fileId = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
  const link: ShareLinkDto = { id: 'l1', fileId, token: 'tok', expiresAtUtc: null, createdAtUtc: '2026-07-22T10:00:00Z' };
  let service: ShareService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ShareService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('should create a link', () => {
    let result: ShareLinkDto | undefined;
    service.create(fileId, '2026-08-01T00:00:00Z').subscribe(r => (result = r));
    const req = http.expectOne(`${environment.apiBaseUrl}/Share`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fileId, expiresAtUtc: '2026-08-01T00:00:00Z' });
    req.flush(link);
    expect(result).toEqual(link);
  });

  it('should list links for a file', () => {
    let result: ShareLinkDto[] | undefined;
    service.list(fileId).subscribe(r => (result = r));
    const req = http.expectOne(r => r.url === `${environment.apiBaseUrl}/Share`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('fileId')).toBe(fileId);
    req.flush([link]);
    expect(result).toEqual([link]);
  });

  it('should revoke a link', () => {
    service.revoke('l1').subscribe();
    const req = http.expectOne(`${environment.apiBaseUrl}/Share/l1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should build the public link', () => {
    expect(service.linkFor('tok')).toBe(`${window.location.origin}/share/tok`);
  });

  it('should fetch public info and bytes anonymously', () => {
    service.publicInfo('tok').subscribe();
    http.expectOne(`${environment.apiBaseUrl}/Public/tok/info`).flush({});
    service.publicDownload('tok').subscribe();
    const req = http.expectOne(`${environment.apiBaseUrl}/Public/tok`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob());
  });
});
