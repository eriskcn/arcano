import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { environment } from '../environments/environment';

const createToken = (claims: Record<string, unknown> = {}): string => {
  const payload = btoa(JSON.stringify({
    exp: Math.floor(Date.now() / 1000) + 3600,
    email: 'person@arcano.test',
    ...claims,
  }));
  return `header.${payload}.signature`;
};

describe('AuthService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should log in and retain the JWT for the browser session', () => {
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const token = createToken();

    auth.login('person@arcano.test', 'secret123').subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/Auth/login`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'person@arcano.test', password: 'secret123' });
    request.flush({ token });

    expect(auth.email()).toBeNull();
    expect(sessionStorage.getItem('arcano.auth.token')).toBe(token);

    let authenticated = false;
    auth.validateSession().subscribe(result => authenticated = result);
    const sessionRequest = http.expectOne(`${environment.apiBaseUrl}/Auth/session`);
    expect(sessionRequest.request.method).toBe('GET');
    sessionRequest.flush({ email: 'person@arcano.test' });

    expect(authenticated).toBe(true);
    expect(auth.email()).toBe('person@arcano.test');
  });

  it('should register using the backend email and password contract', () => {
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);

    auth.register('person@arcano.test', 'secret123').subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/Auth/register`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'person@arcano.test', password: 'secret123' });
    request.flush({ message: 'User registered successfully.' });
    expect(auth.token()).toBeNull();
  });

  it('should discard expired stored tokens', () => {
    sessionStorage.setItem(
      'arcano.auth.token',
      createToken({ exp: Math.floor(Date.now() / 1000) - 1 }),
    );

    const auth = TestBed.inject(AuthService);

    expect(auth.token()).toBeNull();
    expect(sessionStorage.getItem('arcano.auth.token')).toBeNull();
  });

  it('should reject a forged token when the server cannot validate its signature', () => {
    const token = createToken();
    sessionStorage.setItem('arcano.auth.token', token);
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);

    let authenticated: boolean | undefined;
    auth.validateSession().subscribe(result => authenticated = result);
    const request = http.expectOne(`${environment.apiBaseUrl}/Auth/session`);
    expect(request.request.headers.has('Authorization')).toBe(true);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(authenticated).toBe(false);
    expect(auth.token()).toBeNull();
    expect(sessionStorage.getItem('arcano.auth.token')).toBeNull();
  });

  it('should attach the bearer token only to requests for the configured API', () => {
    const token = createToken();
    sessionStorage.setItem('arcano.auth.token', token);
    const http = TestBed.inject(HttpTestingController);
    const client = TestBed.inject(HttpClient);
    const apiUrl = `${environment.apiBaseUrl}/files`;

    client.get(apiUrl).subscribe();
    const apiRequest = http.expectOne(apiUrl);
    expect(apiRequest.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    apiRequest.flush({});

    client.get('https://other.example.test/data').subscribe();
    const externalRequest = http.expectOne('https://other.example.test/data');
    expect(externalRequest.request.headers.has('Authorization')).toBe(false);
    externalRequest.flush({});
  });
});
