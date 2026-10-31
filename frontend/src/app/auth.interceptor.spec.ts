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
