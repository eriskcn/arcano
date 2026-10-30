import { provideHttpClient } from '@angular/common/http';
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
        provideRouter([
          { path: 'app', component: ProtectedPage, canActivate: [authenticatedGuard] },
          { path: 'signin', component: SignInPage, canActivate: [anonymousOnlyGuard] },
        ]),
      ],
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/signin');

    expect(TestBed.inject(Router).url).toBe('/app');
  });
});
