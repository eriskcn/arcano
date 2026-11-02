import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { environment } from '../environments/environment';


describe('authInterceptor with a relative apiBaseUrl', () => {
  const original = environment.apiBaseUrl;
  afterEach(() => { environment.apiBaseUrl = original; });

  beforeEach(() => {
    environment.apiBaseUrl = '/api';
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { tokenForRequest: () => 'tok', needsRefresh: () => false } },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  it('adds the Bearer header to same-origin /api requests only', () => {
    const http = TestBed.inject(HttpClient);
    const ctl = TestBed.inject(HttpTestingController);

    http.get('/api/Files').subscribe();
    expect(ctl.expectOne('/api/Files').request.headers.get('Authorization')).toBe('Bearer tok');

    http.get('/other').subscribe();
    expect(ctl.expectOne('/other').request.headers.has('Authorization')).toBe(false);

    http.get('https://evil.example/api/Files').subscribe();
    expect(ctl.expectOne('https://evil.example/api/Files').request.headers.has('Authorization')).toBe(false);

    ctl.verify();
  });
});

describe('authInterceptor 401 handling', () => {
  const sessionExpired = vi.fn();
  beforeEach(() => {
    sessionExpired.mockClear();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { tokenForRequest: () => 'tok', needsRefresh: () => false, sessionExpired } },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  it('ends the session on a 401 from an authenticated API request', () => {
    const http = TestBed.inject(HttpClient);
    const ctl = TestBed.inject(HttpTestingController);
    let status = 0;
    http.get(`${environment.apiBaseUrl}/Files`).subscribe({ error: e => (status = e.status) });
    ctl.expectOne(`${environment.apiBaseUrl}/Files`).flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
    expect(sessionExpired).toHaveBeenCalledWith('tok');
  });

  it('ignores 401 from credential, session and public calls and non-401 errors', () => {
    const http = TestBed.inject(HttpClient);
    const ctl = TestBed.inject(HttpTestingController);
    for (const path of ['/Auth/login', '/Auth/refresh', '/Auth/session', '/Public/abc/info']) {
      http.get(`${environment.apiBaseUrl}${path}`).subscribe({ error: () => undefined });
      ctl.expectOne(`${environment.apiBaseUrl}${path}`).flush(null, { status: 401, statusText: 'Unauthorized' });
    }
    http.get(`${environment.apiBaseUrl}/Files`).subscribe({ error: () => undefined });
    ctl.expectOne(`${environment.apiBaseUrl}/Files`).flush(null, { status: 403, statusText: 'Forbidden' });
    expect(sessionExpired).not.toHaveBeenCalled();
  });
});
