import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authenticatedGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(AuthService).isAuthenticated()
    ? true
    : router.createUrlTree(['/signin'], { queryParams: { returnUrl: state.url } });
};

export const anonymousOnlyGuard: CanActivateFn = () =>
  inject(AuthService).isAuthenticated()
    ? inject(Router).createUrlTree(['/app'])
    : true;
