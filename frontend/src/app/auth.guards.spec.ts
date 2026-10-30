import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../environments/environment';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { authenticatedGuard, anonymousOnlyGuard } from './auth.guards';

@Component({ standalone: true, template: 'protected' })
class ProtectedPage {}

@Component({ standalone: true, template: 'sign in' })
class SignInPage {}

const createToken = (): string => {
  const claims = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));
  return `header.${claims}.signature`;
};

describe('authentication route guards', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it('should redirect unauthenticated visits to protected routes to sign in', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'app', component: ProtectedPage, canActivate: [authenticatedGuard] },
          { path: 'signin', component: SignInPage, canActivate: [anonymousOnlyGuard] },
        ]),
      ],
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/app');

    expect(TestBed.inject(Router).url).toBe('/signin?returnUrl=%2Fapp');
  });

  it('should redirect authenticated visits from sign in to the app', async () => {
    sessionStorage.setItem('arcano.auth.token', createToken());
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'app', component: ProtectedPage, canActivate: [authenticatedGuard] },
          { path: 'signin', component: SignInPage, canActivate: [anonymousOnlyGuard] },
        ]),
      ],
    });

    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/signin');
    await new Promise(resolve => setTimeout(resolve, 0));
    const request = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiBaseUrl}/Auth/session`,
    );
    request.flush({ email: 'person@arcano.test' });
    await navigation;

    expect(TestBed.inject(Router).url).toBe('/app');
  });

  it('should redirect a forged token to sign in after server validation fails', async () => {
    sessionStorage.setItem('arcano.auth.token', createToken());
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'app', component: ProtectedPage, canActivate: [authenticatedGuard] },
          { path: 'signin', component: SignInPage, canActivate: [anonymousOnlyGuard] },
        ]),
      ],
    });

    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/app');
    await new Promise(resolve => setTimeout(resolve, 0));
    const request = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiBaseUrl}/Auth/session`,
    );
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    await navigation;

    expect(TestBed.inject(Router).url).toBe('/signin?returnUrl=%2Fapp');
    expect(sessionStorage.getItem('arcano.auth.token')).toBeNull();
  });
});
