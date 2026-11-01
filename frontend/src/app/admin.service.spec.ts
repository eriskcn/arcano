import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminService } from './admin.service';
import { environment } from '../environments/environment';

describe('AdminService', () => {
  const base = `${environment.apiBaseUrl}/Admin`;
  let http: HttpTestingController;
  let admin: AdminService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    admin = TestBed.inject(AdminService);
  });

  afterEach(() => http.verify());

  const expect204 = (method: string, url: string, body: unknown = null) => {
    const request = http.expectOne(url);
    expect(request.request.method).toBe(method);
    expect(request.request.body).toEqual(body);
    request.flush(null, { status: 204, statusText: 'No Content' });
  };

  it('lists users and keys', () => {
    admin.users().subscribe();
    admin.keys().subscribe();
    expect(http.expectOne(`${base}/users`).request.method).toBe('GET');
    expect(http.expectOne(`${base}/keys`).request.method).toBe('GET');
  });

  it('sets the quota, with null for the server default', () => {
    admin.setQuota('u1', 5).subscribe();
    expect204('PUT', `${base}/users/u1/quota`, { quotaBytes: 5 });
    admin.setQuota('u1', null).subscribe();
    expect204('PUT', `${base}/users/u1/quota`, { quotaBytes: null });
  });

  it('disables, enables and deletes a user', () => {
    admin.disable('u1').subscribe();
    expect204('POST', `${base}/users/u1/disable`);
    admin.enable('u1').subscribe();
    expect204('POST', `${base}/users/u1/enable`);
    admin.deleteUser('u1').subscribe();
    expect204('DELETE', `${base}/users/u1`);
  });

  it('creates and revokes keys', () => {
    let key = '';
    admin.createKey({ type: 'Reset', userId: 'u1', expiresInDays: 2 }).subscribe(created => (key = created.key));
    const request = http.expectOne(`${base}/keys`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ type: 'Reset', userId: 'u1', expiresInDays: 2 });
    request.flush({ id: 'k1', key: 'plain', type: 'Reset', expiresAtUtc: null }, { status: 201, statusText: 'Created' });
    expect(key).toBe('plain');

    admin.revokeKey('k1').subscribe();
    expect204('DELETE', `${base}/keys/k1`);
  });
});
